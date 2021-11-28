module FSharpLanguageServer.Conversions

open LSP.Log
open FSharp.Compiler
open FSharp.Compiler.CodeAnalysis 
open FSharp.Compiler.Symbols
open FSharp.Compiler.EditorServices
open System
open System.IO
open LSP.Types
open FSharp.Data
open FSharp.Compiler.Diagnostics
open FSharp.Compiler
open TipFormatter
open LSP.BaseTypes
/// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Range'
let private errorAsRange(err: FSharpDiagnostic): Range =
    {
        // Got error "The field, constructor or member 'StartLine' is not defined"
        start = {line=err.StartLine-1; character=err.StartColumn}
        ``end`` = {line=err.EndLine-1; character=err.EndColumn}
    }

/// Convert an F# `Range.pos` to an LSP `Position`
let private asPosition(p: Text.pos): Position =
    {
        line=p.Line-1
        character=p.Column
    }
let private toPosition(p: Position): Text.Position =
    Text.Position.mkPos(p.line+1) p.character
    
/// Convert an F# `Range.range` to an LSP `Range`
let asRange(r: Text.range): Range =
    {
        start=asPosition r.Start
        ``end``=asPosition r.End
    }
/// Convert an F# `Range.range` to an LSP `Range`
let toRange str (r: Range): Text.Range =
    Text.Range.mkRange str (toPosition r.start) (toPosition r.``end`` )
    

/// Convert an F# `Range.range` to an LSP `Location`
let private asLocation(l: Text.range): Location =
    {
        uri=Uri("file://" + l.FileName)
        range = asRange l
    }

/// Convert an F# Compiler Services 'FSharpErrorSeverity' to an LSP 'DiagnosticSeverity'
let private asDiagnosticSeverity(s: FSharpDiagnosticSeverity): DiagnosticSeverity =
    match s with
    | FSharpDiagnosticSeverity.Warning -> DiagnosticSeverity.Warning
    | FSharpDiagnosticSeverity.Error -> DiagnosticSeverity.Error
    | FSharpDiagnosticSeverity.Info -> DiagnosticSeverity.Information

/// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Diagnostic'
let asDiagnostic(err: FSharpDiagnostic): Diagnostic =
    {
        range = errorAsRange(err)
        severity = Some(asDiagnosticSeverity(err.Severity))
        code = Some(sprintf "%d: %s" err.ErrorNumber err.Subcategory)
        source = None
        message = err.Message
    }

/// Create a Diagnostic
let diagnostic(message: string, range: Text.range, severity: DiagnosticSeverity): Diagnostic =
    {
        range = asRange(range)
        severity = Some(severity)
        code = None
        source = None
        message = message
    }

/// Some compiler errors have no location in the file and should be displayed at the top of the file
let private hasNoLocation(err: FSharpDiagnostic): bool =
    err.StartLine-1 = 0 &&
    err.StartColumn = 0 &&
    err.StartLine-1 = 0 &&
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
let asDiagnostics(errors: FSharpDiagnostic seq): Diagnostic list =
    [
        for err in errors do
            if hasNoLocation(err) then
                yield errorAtTop(sprintf "%s: %s" err.Subcategory err.Message)
            else
                yield asDiagnostic(err)
    ]


/// Convert an F# `ToolTipElement` to an LSP `Hover`
let asHover(ToolTipText tips): Hover =
    let elements =
        [ for t in tips do
            match t with
            | ToolTipElement.CompositionError(e) -> dprintfn "Error rendering tooltip: %s" e
            | ToolTipElement.None -> ()
            | ToolTipElement.Group(elements) ->
                yield! elements ]
    let contents =
        match elements with
        | [] -> []
        | [one] ->
            [   yield HighlightedString(one.MainDescription|>formatTaggedTexts, "fsharp") //TODO: see how FSAC deals with this with th updated compiler stuff
                match TipFormatter.docComment(one.XmlDoc) with
                | None -> ()
                | Some(markdown) -> yield PlainString(markdown + "\n\n")
                match one.Remarks with
                | None | Some([||])-> ()
                | Some(remarks) ->
                    yield PlainString("*" + (remarks|>formatTaggedTexts) + "*\n\n") ]
        | many ->
            let last = List.last(many)
            [   for e in many do
                    yield HighlightedString(e.MainDescription|>formatTaggedTexts, "fsharp")
                match TipFormatter.docComment(last.XmlDoc) with
                | None -> ()
                | Some(markdown) -> yield PlainString(markdown)
                match last.Remarks with
                | None | Some([||]) -> ()
                | Some(remarks) ->
                    yield PlainString("*" + (remarks|>formatTaggedTexts) + "*\n\n") ]
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

/// Convert an F# `DeclarationListItem` to an LSP `CompletionItem`
let private asCompletionItem(i: DeclarationListItem): CompletionItem =
    { defaultCompletionItem with
        label = i.Name
        insertText = Some(i.NameInCode)
        kind = Some(asCompletionItemKind(i.Glyph))
        detail = Some(i.FullName)
        // Stash FullName in data so we can use it later in ResolveCompletionItem
        data = JsonValue.Record [|"FullName", JsonValue.String(i.FullName)|]
    }

/// Convert an F# `DeclarationListInfo` to an LSP `CompletionList`
/// Used in rendering autocomplete lists
let asCompletionList(ds: DeclarationListInfo): CompletionList =
    let items = [for i in ds.Items do yield asCompletionItem(i)]
    {isIncomplete=List.isEmpty(items); items=items}

/// Convert an F# `MethodGroupItemParameter` to an LSP `ParameterInformation`
let private asParameterInformation(p: MethodGroupItemParameter): ParameterInformation =
    {
        label = p.ParameterName
        documentation = Some (p.Display|>formatTaggedTexts )//TODO: find out what to do with taggedtext
    }

/// Convert an F# method name + `MethodGroupItem` to an LSP `SignatureInformation`
/// Used in providing signature help after autocompleting
let asSignatureInformation(methodName: string, s: MethodGroupItem): SignatureInformation =
    let doc = match s.Description with
                | ToolTipText [ToolTipElement.Group [tip]] -> Some tip.MainDescription
                | _ ->
                    dprintfn "Can't render documentation %A" s.Description
                    None
    let xmlDocs=
        match TipFormatter.docComment(s.XmlDoc) with
        | None -> ""
        | Some(markdown) ->  markdown
    let parameterName(p: MethodGroupItemParameter) = p.ParameterName
    let parameterNames = Array.map parameterName s.Parameters
    {
        label = sprintf "%s(%s)" methodName (String.concat ", " parameterNames)
        documentation = doc|>Option.map (fun x-> (x|>formatTaggedTexts)+"\n⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯\n"+xmlDocs)
        parameters = Array.map asParameterInformation s.Parameters |> List.ofArray
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
    asLocation(s.Range)

/// Convert an F# `NavigationItemKind` to an LSP `SymbolKind`
/// `NavigationItemKind` is the level of symbol-type information you get when parsing without typechecking
let private asSymbolKind(k: NavigationItemKind): SymbolKind =
    match k with
    | NavigationItemKind.Namespace -> SymbolKind.Namespace
    | NavigationItemKind.ModuleFile -> SymbolKind.Module
    | NavigationItemKind.Exception -> SymbolKind.Class
    | NavigationItemKind.Module -> SymbolKind.Module
    | NavigationItemKind.Type -> SymbolKind.Interface
    | NavigationItemKind.Method -> SymbolKind.Method
    | PropertyDecl -> SymbolKind.Property
    | NavigationItemKind.Field -> SymbolKind.Field
    | NavigationItemKind.Other -> SymbolKind.Variable

/// Convert an F# `NavigationDeclarationItem` to an LSP `SymbolInformation`
/// `NavigationDeclarationItem` is the parsed AST representation of a symbol without typechecking
/// `container` is present when `d` is part of a module or type
let asSymbolInformation(d: MyNavigationItem, container: MyNavigationItem option): SymbolInformation =
    let declarationName(d: MyNavigationItem) = d.Name
    {
        name=d.Name
        kind=asSymbolKind d.Kind
        location=asLocation d.Range
        containerName=Option.map declarationName container
    }

/// Convert symbols declared in an .fsi file to a CodeLens that helps the user navigate to the definition
let asGoToImplementation(name: string list, file: FileInfo, range: Text.range): CodeLens =
    let jsonFile = JsonValue.String(file.FullName)
    let jsonName = JsonValue.Array([|for i in name do yield JsonValue.String(i)|])
    {
        range=asRange(range)
        command=None
        data=JsonValue.Array([|jsonFile; jsonName|])
    }

let goToImplementationData(goTo: CodeLens) =
    match goTo.data with
    | JsonValue.Array([|JsonValue.String(file); JsonValue.Array(jsonNames)|]) ->
        FileInfo(file), [for JsonValue.String(j) in jsonNames do yield j ]

let resolveGoToImplementation(unresolved: CodeLens, file: FileInfo, range: Text.range): CodeLens =
    let command =
        {
            title=sprintf "%s(%d)" file.Name range.StartLine
            command="fsharp.command.goto"
            arguments=[
                JsonValue.String(file.FullName)
                JsonValue.Number(decimal(range.StartLine - 1))
                JsonValue.Number(decimal(range.StartColumn))
                JsonValue.Number(decimal(range.EndLine - 1))
                JsonValue.Number(decimal(range.EndColumn))
            ]
        }
    { unresolved with command = Some(command) }

let resolveMissingGoToImplementation(unresolved: CodeLens, file: FileInfo): CodeLens =
    let command =
        {
            title="Not Found"
            command="fsharp.command.goto"
            arguments=[
                JsonValue.String(file.FullName)
                JsonValue.Number(decimal(unresolved.range.start.line))
                JsonValue.Number(decimal(unresolved.range.start.character))
                JsonValue.Number(decimal(unresolved.range.``end``.line))
                JsonValue.Number(decimal(unresolved.range.``end``.character))
            ]
        }
    { unresolved with command = Some(command) }

let asRunTest(fsproj: FileInfo, fullyQualifiedName: string list, test: Syntax.SynBinding): CodeLens =
    {
        range=asRange(test.RangeOfBindingWithoutRhs)
        command=Some({  title="Run Test"
                        command="fsharp.command.test.run"
                        arguments=[JsonValue.String(fsproj.FullName); JsonValue.String(String.concat "." fullyQualifiedName)] })
        data=JsonValue.Null
    }

let asDebugTest(fsproj: FileInfo, fullyQualifiedName: string list, test: Syntax.SynBinding): CodeLens =
    {
        range=asRange(test.RangeOfBindingWithoutRhs)
        command=Some({  title="Debug Test"
                        command="fsharp.command.test.debug"
                        arguments=[JsonValue.String(fsproj.FullName); JsonValue.String(String.concat "." fullyQualifiedName)] })
        data=JsonValue.Null
    }
