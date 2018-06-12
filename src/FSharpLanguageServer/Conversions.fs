module FSharpLanguageServer.Conversions 

open LSP.Log
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open System
open System.IO
open LSP.Types
open FSharp.Data

module Ast = Microsoft.FSharp.Compiler.Ast

/// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Range'
let private errorAsRange(err: FSharpErrorInfo): Range = 
    {
        // Got error "The field, constructor or member 'StartLine' is not defined"
        start = {line=err.StartLineAlternate-1; character=err.StartColumn}
        ``end`` = {line=err.EndLineAlternate-1; character=err.EndColumn}
    }

/// Convert an F# Compiler Services 'FSharpErrorSeverity' to an LSP 'DiagnosticSeverity'
let private asDiagnosticSeverity(s: FSharpErrorSeverity): DiagnosticSeverity =
    match s with 
    | FSharpErrorSeverity.Warning -> DiagnosticSeverity.Warning 
    | FSharpErrorSeverity.Error -> DiagnosticSeverity.Error 

/// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Diagnostic'
let asDiagnostic(err: FSharpErrorInfo): Diagnostic = 
    {
        range = errorAsRange(err)
        severity = Some(asDiagnosticSeverity err.Severity)
        code = Some(sprintf "%d: %s" err.ErrorNumber err.Subcategory)
        source = None
        message = err.Message
    }
    
/// Some compiler errors have no location in the file and should be displayed at the top of the file
let private hasNoLocation(err: FSharpErrorInfo): bool = 
    err.StartLineAlternate-1 = 0 && 
    err.StartColumn = 0 &&
    err.EndLineAlternate-1 = 0 &&
    err.EndColumn = 0

/// A special error message that shows at the top of the file
let errorAtTop(message: string): Diagnostic =
    {
        range = { start = {line=0; character=0}; ``end`` = {line=0; character=1} }
        severity = Some(DiagnosticSeverity.Error) 
        code = None
        source = None 
        message = message
    }

/// Convert a list of F# Compiler Services 'FSharpErrorInfo' to LSP 'Diagnostic'
let asDiagnostics(errors: FSharpErrorInfo seq): Diagnostic list =
    [ 
        for err in errors do 
            if hasNoLocation(err) then 
                yield errorAtTop(sprintf "%s: %s" err.Subcategory err.Message)
            else
                yield asDiagnostic(err) 
    ]


/// Convert an F# `FSharpToolTipElement` to an LSP `Hover`
let asHover(FSharpToolTipText tips): Hover = 
    let elements = 
        [ for t in tips do
            match t with 
            | FSharpToolTipElement.CompositionError(e) -> dprintfn "Error rendering tooltip: %s" e
            | FSharpToolTipElement.None -> () 
            | FSharpToolTipElement.Group elements -> 
                yield! elements ]
    let contents = 
        match elements with 
        | [] -> []
        | [one] -> 
            [   yield HighlightedString(one.MainDescription, "fsharp") 
                match TipFormatter.docComment(one.XmlDoc) with 
                | None -> ()
                | Some(markdown) -> yield PlainString(markdown) ]
        | many -> 
            let last = List.last(many)
            [   for e in many do 
                    yield HighlightedString(e.MainDescription, "fsharp")
                match TipFormatter.docSummaryOnly(last.XmlDoc) with 
                | None -> ()
                | Some(markdown) -> yield PlainString(markdown) ]
    {contents=contents; range=None}

/// Convert an F# `FSharpGlyph` to an LSP `CompletionItemKind`
let private asCompletionItemKind(k: FSharpGlyph): CompletionItemKind = 
    match k with 
    | FSharpGlyph.Class -> CompletionItemKind.Class
    | FSharpGlyph.Constant -> CompletionItemKind.Constant
    | FSharpGlyph.Delegate -> CompletionItemKind.Property // ?
    | FSharpGlyph.Enum -> CompletionItemKind.Enum
    | FSharpGlyph.EnumMember -> CompletionItemKind.EnumMember
    | FSharpGlyph.Event -> CompletionItemKind.Event
    | FSharpGlyph.Exception -> CompletionItemKind.Class // ?
    | FSharpGlyph.Field -> CompletionItemKind.Field
    | FSharpGlyph.Interface -> CompletionItemKind.Interface
    | FSharpGlyph.Method -> CompletionItemKind.Method
    | FSharpGlyph.OverridenMethod -> CompletionItemKind.Method
    | FSharpGlyph.Module -> CompletionItemKind.Module
    | FSharpGlyph.NameSpace -> CompletionItemKind.Module // ?
    | FSharpGlyph.Property -> CompletionItemKind.Property
    | FSharpGlyph.Struct -> CompletionItemKind.Struct
    | FSharpGlyph.Typedef -> CompletionItemKind.Interface // ?
    | FSharpGlyph.Type -> CompletionItemKind.Class // ?
    | FSharpGlyph.Union -> CompletionItemKind.Enum // ?
    | FSharpGlyph.Variable -> CompletionItemKind.Variable
    | FSharpGlyph.ExtensionMethod -> CompletionItemKind.Method
    | FSharpGlyph.Error -> CompletionItemKind.Class  // ?

/// Convert an F# `FSharpDeclarationListItem` to an LSP `CompletionItem`
let private asCompletionItem(i: FSharpDeclarationListItem): CompletionItem = 
    { defaultCompletionItem with 
        label = i.Name 
        kind = Some(asCompletionItemKind(i.Glyph))
        detail = Some(i.FullName)
        // Stash FullName in data so we can use it later in ResolveCompletionItem
        data = JsonValue.Record [|"FullName", JsonValue.String(i.FullName)|]
    }

/// Convert an F# `FSharpDeclarationListInfo` to an LSP `CompletionList`
/// Used in rendering autocomplete lists
let asCompletionList(ds: FSharpDeclarationListInfo): CompletionList = 
    let items = [for i in ds.Items do yield asCompletionItem(i)]
    {isIncomplete=List.isEmpty(items); items=items}

/// Convert an F# `FSharpMethodGroupItemParameter` to an LSP `ParameterInformation`
let private asParameterInformation(p: FSharpMethodGroupItemParameter): ParameterInformation = 
    {
        label = p.ParameterName
        documentation = Some p.Display
    }

/// Convert an F# method name + `FSharpMethodGroupItem` to an LSP `SignatureInformation`
/// Used in providing signature help after autocompleting
let asSignatureInformation(methodName: string, s: FSharpMethodGroupItem): SignatureInformation = 
    let doc = match s.Description with 
                | FSharpToolTipText [FSharpToolTipElement.Group [tip]] -> Some tip.MainDescription 
                | _ -> 
                    dprintfn "Can't render documentation %A" s.Description 
                    None 
    let parameterName(p: FSharpMethodGroupItemParameter) = p.ParameterName
    let parameterNames = Array.map parameterName s.Parameters
    {
        label = sprintf "%s(%s)" methodName (String.concat ", " parameterNames) 
        documentation = doc 
        parameters = Array.map asParameterInformation s.Parameters |> List.ofArray
    }


/// Convert an F# `Range.pos` to an LSP `Position`
let private asPosition(p: Range.pos): Position = 
    {
        line=p.Line-1
        character=p.Column
    }

/// Convert an F# `Range.range` to an LSP `Range`
let asRange(r: Range.range): Range = 
    {
        start=asPosition r.Start
        ``end``=asPosition r.End
    }

/// Convert an F# `Range.range` to an LSP `Location`
let private asLocation(l: Range.range): Location = 
    { 
        uri=Uri("file://" + l.FileName)
        range = asRange l 
    }

/// Get the lcation where `s` was declared
let declarationLocation(s: FSharpSymbol): Location option = 
    match s.DeclarationLocation with 
    | None -> 
        dprintfn "Symbol %s has no declaration" s.FullName 
        None 
    | Some l ->
        Some(asLocation(l))

/// Get the location where `s` was used
let useLocation(s: FSharpSymbolUse): Location = 
    asLocation(s.RangeAlternate)

/// Convert an F# `FSharpNavigationDeclarationItemKind` to an LSP `SymbolKind`
/// `FSharpNavigationDeclarationItemKind` is the level of symbol-type information you get when parsing without typechecking
let private asSymbolKind(k: FSharpNavigationDeclarationItemKind): SymbolKind = 
    match k with 
    | NamespaceDecl -> SymbolKind.Namespace
    | ModuleFileDecl -> SymbolKind.Module
    | ExnDecl -> SymbolKind.Class
    | ModuleDecl -> SymbolKind.Module
    | TypeDecl -> SymbolKind.Interface
    | MethodDecl -> SymbolKind.Method
    | PropertyDecl -> SymbolKind.Property
    | FieldDecl -> SymbolKind.Field
    | OtherDecl -> SymbolKind.Variable

/// Convert an F# `FSharpNavigationDeclarationItem` to an LSP `SymbolInformation`
/// `FSharpNavigationDeclarationItem` is the parsed AST representation of a symbol without typechecking
/// `container` is present when `d` is part of a module or type
let asSymbolInformation(d: FSharpNavigationDeclarationItem, container: FSharpNavigationDeclarationItem option): SymbolInformation = 
    let declarationName(d: FSharpNavigationDeclarationItem) = d.Name
    {
        name=d.Name 
        kind=asSymbolKind d.Kind 
        location=asLocation d.Range 
        containerName=Option.map declarationName container
    }

let asRunTest(fsproj: FileInfo, fullyQualifiedName: string list, test: Ast.SynBinding): CodeLens =
    {
        range=asRange(test.RangeOfBindingSansRhs)
        command=Some({  title="Run Test"
                        command="fsharp.test.run"
                        arguments=[JsonValue.String(fsproj.FullName); JsonValue.String(String.concat "." fullyQualifiedName)] })
        data=JsonValue.Null
    }