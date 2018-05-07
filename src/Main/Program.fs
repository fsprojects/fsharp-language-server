module Main.Program

open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open System
open System.IO
open LSP
open LSP.Types
open System.Text.RegularExpressions

let private TODO() = raise (Exception "TODO")

// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Range'
let private asRange (err: FSharpErrorInfo): Range = 
    {
        // Got error "The field, constructor or member 'StartLine' is not defined"
        start = {line=err.StartLineAlternate-1; character=err.StartColumn}
        ``end`` = {line=err.EndLineAlternate-1; character=err.EndColumn}
    }

// Convert an F# Compiler Services 'FSharpErrorSeverity' to an LSP 'DiagnosticSeverity'
let private asDiagnosticSeverity(s: FSharpErrorSeverity): DiagnosticSeverity =
    match s with 
    | FSharpErrorSeverity.Warning -> DiagnosticSeverity.Warning 
    | FSharpErrorSeverity.Error -> DiagnosticSeverity.Error 

// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Diagnostic'
let private asDiagnostic (err: FSharpErrorInfo): Diagnostic = 
    {
        range = asRange(err)
        severity = Some (asDiagnosticSeverity err.Severity)
        code = Some (sprintf "%d: %s" err.ErrorNumber err.Subcategory)
        source = None
        message = err.Message
    }
    
// Some compiler errors have no location in the file and should be logged separately
let private hasNoLocation (err: FSharpErrorInfo): bool = 
    err.StartLineAlternate-1 = 0 && 
    err.StartColumn = 0 &&
    err.EndLineAlternate-1 = 0 &&
    err.EndColumn = 0
    
// Log no-location messages once and then silence them
let private alreadyLogged = System.Collections.Generic.HashSet<string>()
let private logOnce (message: string): unit = 
    if not (alreadyLogged.Contains message) then 
        eprintfn "%s" message 
        alreadyLogged.Add(message) |> ignore

// Convert a list of F# Compiler Services 'FSharpErrorInfo' to LSP 'Diagnostic'
let private convertDiagnostics (errors: FSharpErrorInfo[]): Diagnostic list =
    [ 
        for err in errors do 
            if hasNoLocation err then 
                logOnce(sprintf "NOPOS %s %d %s '%s'" err.FileName err.ErrorNumber err.Subcategory err.Message)
            else
                yield asDiagnostic(err) 
    ]

// A special error message that shows at the top of the file
let private errorAtTop (message: string): Diagnostic =
    {
        range = { start = {line=0; character=0}; ``end`` = {line=0; character=1} }
        severity = Some DiagnosticSeverity.Error 
        code = None
        source = None 
        message = message
    }

// Look for a fully qualified name leading up to the cursor
let findNamesUnderCursor (lineContent: string) (character: int): string list = 
    let r = Regex(@"(\w+|``[^`]+``)([\.?](\w+|``[^`]+``))*")
    let ms = r.Matches(lineContent)
    let overlaps (m: Match) = m.Index <= character && character <= m.Index + m.Length 
    let found: Match list = [ for m in ms do if overlaps m then yield m ]
    match found with 
    | [] -> 
        eprintfn "No identifiers at %d in line %s" character lineContent
        [] 
    | single::[] -> 
        let r = Regex(@"(\w+|``[^`]+``)")
        let ms = r.Matches(single.Value)
        let result = [ for m in ms do 
                            if single.Index + m.Index < character then 
                                if m.Value.StartsWith("``") then
                                    yield m.Value.Substring(2, m.Value.Length - 4)
                                else
                                    yield m.Value ]
        eprintfn "Found identifier under cursor %A" result
        result
    | multiple -> 
        eprintfn "Line %s offset %d matched multiple groups %A" lineContent character multiple 
        []

// Look for a method call like foo.MyMethod() before the cursor
let findMethodCallBeforeCursor (lineContent: string) (cursor: int): int option = 
    let mutable found = -1
    let mutable parenDepth = 0
    for i in (min (cursor-1) lineContent.Length) .. -1 .. 0 do 
        match lineContent.[i] with 
        | ')' -> parenDepth <- parenDepth + 1
        | '(' when parenDepth > 0 -> parenDepth <- parenDepth - 1
        | '(' when found = -1 -> found <- i
        | _ -> ()
    if found = -1 then None 
    else 
        let prefix = lineContent.Substring(0, found).TrimEnd()
        if Regex(@"let[ \w]+$").IsMatch(prefix) then 
            eprintfn "No signature help in let expression %s" lineContent 
            None 
        else if Regex(@"member[ \w\.]+$").IsMatch(prefix) then 
            eprintfn "No signature help in member expression %s" lineContent 
            None 
        else Some prefix.Length

// Figure out the active parameter by counting ',' characters
let countCommas (lineContent: string) (endOfMethodName: int) (cursor: int): int = 
    let mutable count = 0
    for i in endOfMethodName .. (min (cursor-1) lineContent.Length) do 
        if lineContent.[i] = ',' then 
            count <- count + 1
    count

// Convert an F# `FSharpToolTipElement` to an LSP `Hover`
let private asHover (FSharpToolTipText tips): Hover = 
    let convert = 
        [ for t in tips do
            match t with 
            | FSharpToolTipElement.None -> () 
            | FSharpToolTipElement.Group elements -> 
                for e in elements do 
                    yield HighlightedString(e.MainDescription, "fsharp")
            | FSharpToolTipElement.CompositionError err -> 
                eprintfn "Tooltip error %s" err]
    {contents=convert; range=None}

let private asDocumentation (FSharpToolTipText tips): string option = 
    match tips with 
    | [FSharpToolTipElement.Group [e]] -> Some e.MainDescription
    | _ -> None // When there are zero or multiple overloads, don't display docs

let private convertCompletionItemKind (k: Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind): CompletionItemKind option = 
    match k with 
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Field -> Some CompletionItemKind.Field
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Property -> Some CompletionItemKind.Property
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Method isExtension -> Some CompletionItemKind.Method
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Event -> None
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Argument -> Some CompletionItemKind.Variable
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Other -> None

let private convertDeclaration (i: FSharpDeclarationListItem): CompletionItem = 
    { defaultCompletionItem with 
        label = i.Name 
        kind = convertCompletionItemKind i.Kind
        detail = Some i.FullName
        documentation = asDocumentation i.DescriptionText
    }

let private convertDeclarations (ds: FSharpDeclarationListInfo): CompletionList = 
    let items = List.map convertDeclaration (List.ofArray ds.Items)
    {isIncomplete=false; items=items}

let private convertParameter (p: FSharpMethodGroupItemParameter): ParameterInformation = 
    {
        label = p.ParameterName
        documentation = Some p.Display
    }

let private convertSignature (methodName: string) (s: FSharpMethodGroupItem): SignatureInformation = 
    let doc = match s.Description with 
                | FSharpToolTipText [FSharpToolTipElement.Group [tip]] -> Some tip.MainDescription 
                | _ -> 
                    eprintfn "Can't render documentation %A" s.Description 
                    None 
    let parameterName (p: FSharpMethodGroupItemParameter) = p.ParameterName
    let parameterNames = Array.map parameterName s.Parameters
    {
        label = sprintf "%s(%s)" methodName (String.concat ", " parameterNames) 
        documentation = doc 
        parameters = Array.map convertParameter s.Parameters |> List.ofArray
    }

// Check if candidate contains all the characters of find, in-order, case-insensitive
// candidate is allowed to have other characters in between, as long as it contains all of find in-order
let private containsChars (find: string) (candidate: string): bool = 
    let mutable iFind = 0
    for c in candidate do 
        if iFind < find.Length && c = find.[iFind] then iFind <- iFind + 1
    iFind = find.Length

let private matchesQuery (query: string) (candidate: FSharpSymbol): bool = 
    containsChars (query.ToLower()) (candidate.FullName.ToLower())

// FSharpEntity, FSharpUnionCase
/// FSharpField, FSharpGenericParameter, FSharpStaticParameter, FSharpMemberOrFunctionOrValue, FSharpParameter,
/// or FSharpActivePatternCase.
let private symbolKind (s: FSharpSymbol): SymbolKind = 
    match s with 
    | :? FSharpEntity as x -> 
        if x.IsFSharpModule then SymbolKind.Module 
        else if x.IsNamespace then SymbolKind.Namespace 
        else if x.IsClass then SymbolKind.Class
        else if x.IsEnum then SymbolKind.Enum 
        else if x.IsInterface then SymbolKind.Interface 
        else SymbolKind.Variable 
    | :? FSharpUnionCase as x -> SymbolKind.Constant
    | :? FSharpField as x -> SymbolKind.Field
    | :? FSharpGenericParameter as x -> SymbolKind.Interface
    | :? FSharpStaticParameter as x -> SymbolKind.Variable 
    | :? FSharpMemberOrFunctionOrValue as x -> 
        if x.IsConstructor then SymbolKind.Constructor 
        else if x.IsTypeFunction then SymbolKind.Function
        else if x.IsValue then SymbolKind.Property 
        else if x.IsProperty then SymbolKind.Property 
        else if x.IsMember then SymbolKind.Method 
        else SymbolKind.Function
    | :? FSharpParameter as x -> SymbolKind.Variable
    | :? FSharpActivePatternCase as x -> SymbolKind.Constant

let private containerName (s: FSharpSymbol): string option = 
    if s.FullName = s.DisplayName then 
        None
    else if s.FullName.EndsWith("." + s.DisplayName) then 
        Some(s.FullName.Substring(0, s.FullName.Length - s.DisplayName.Length - 1))
    else 
        Some s.FullName

let private asPosition (p: Range.pos): Position = 
    {line=p.Line-1; character = p.Column}

let private asLocation (s: FSharpSymbol): Location = 
    let l = s.DeclarationLocation.Value
    let uri = Uri("file://" + l.FileName)
    {
        uri=uri
        range = 
        {
            start=asPosition l.Start
            ``end``=asPosition l.End
        }
    }

let private symbolInformation (s: FSharpSymbol): SymbolInformation = 
    {
        name = s.DisplayName
        containerName = containerName s
        kind = symbolKind(s)
        location = asLocation(s)
    }

// TODO actually consider types
let private findCompatibleOverload (activeParameter: int) (methods: FSharpMethodGroupItem[]): int option = 
    let mutable result = -1 
    for i in 0 .. methods.Length - 1 do 
        if result = -1 && (activeParameter = 0 || activeParameter < methods.[i].Parameters.Length) then 
            result <- i 
    if result = -1 then None else Some result

type private FindFile = 
    | NoSourceFile of sourcePath: string
    | NoProjectFile of sourcePath: string
    | NotInProjectOptions of sourcePath: string * projectOptions: FSharpProjectOptions 
    | Found of sourcePath: string * sourceVersion: int * sourceText: string * projectOptions: FSharpProjectOptions 

type private CheckFile = 
    | Errors of Diagnostic list
    | Ok of parseResult: FSharpParseFileResults * checkResult: FSharpCheckFileResults * errors: Diagnostic list

type Server(client: ILanguageClient) = 
    let docs = DocumentStore()
    let projects = ProjectManager()
    let checker = FSharpChecker.Create()
    // Find a file and its .fsproj context
    let find (uri: Uri): FindFile = 
        let sourcePath = uri.AbsolutePath.ToString()
        let source = docs.Get uri
        let projectOptions = projects.FindProjectFile uri |> Option.map projects.FindProjectOptions
        match source, projectOptions with 
        | None, _ -> NoSourceFile sourcePath
        | _, None -> NoProjectFile sourcePath
        | Some(sourceText, sourceVersion), Some projectOptions -> 
            if Array.contains sourcePath projectOptions.SourceFiles then 
                Found(sourcePath, sourceVersion, sourceText, projectOptions) 
            else 
                NotInProjectOptions(sourcePath, projectOptions)
    // Find a file and check it
    let check (uri: Uri): CheckFile = 
        eprintfn "Check %O" uri
        async {
            match find uri with 
            | NoSourceFile sourcePath -> 
                return Errors [errorAtTop (sprintf "No source file %s" sourcePath )]
            | NoProjectFile sourcePath -> 
                return Errors [errorAtTop (sprintf "No project file for source %s" sourcePath)]
            | NotInProjectOptions(sourcePath, projectOptions) -> 
                return Errors [errorAtTop (sprintf "Not in project %s" projectOptions.ProjectFileName)]
            | Found(sourcePath, sourceVersion, sourceText, projectOptions) -> 
                let! parseResult, checkAnswer = checker.ParseAndCheckFileInProject(sourcePath, sourceVersion, sourceText, projectOptions)
                let parseErrors = convertDiagnostics parseResult.Errors
                match checkAnswer with 
                | FSharpCheckFileAnswer.Aborted -> 
                    eprintfn "Aborted checking %s" sourcePath 
                    return Errors parseErrors
                | FSharpCheckFileAnswer.Succeeded checkResult -> 
                    let checkErrors = convertDiagnostics checkResult.Errors 
                    let allErrors = parseErrors@checkErrors 
                    return Ok(parseResult, checkResult, allErrors)
        } |> Async.RunSynchronously
    // Check a file and send all errors to the client
    let lint (uri: Uri): unit = 
        match check uri with 
        | Errors errors -> client.PublishDiagnostics({uri=uri; diagnostics=errors})
        | Ok(parseResult, checkResult, errors) -> client.PublishDiagnostics({uri=uri; diagnostics=errors})
    interface ILanguageServer with 
        member this.Initialize(p: InitializeParams): InitializeResult = 
            { capabilities = 
                { defaultServerCapabilities with 
                    hoverProvider = true
                    completionProvider = Some({resolveProvider=false; triggerCharacters=['.']})
                    signatureHelpProvider = Some({triggerCharacters=['('; ',']})
                    workspaceSymbolProvider = true
                    textDocumentSync = 
                        { defaultTextDocumentSyncOptions with 
                            openClose = true 
                            save = Some { includeText = false }
                            change = TextDocumentSyncKind.Incremental } } }
        member this.Initialized(): unit = 
            ()
        member this.Shutdown(): unit = 
            ()
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams): unit =
            eprintfn "New configuration %s" (p.ToString())
        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams): unit = 
            docs.Open p
            lint p.textDocument.uri
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams): unit = 
            docs.Change p
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams): unit = TODO()
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): list<TextEdit> = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams): unit = 
            lint p.textDocument.uri
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams): unit = 
            docs.Close p
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): unit = 
            for change in p.changes do 
                eprintfn "Watched file %s %s" (change.uri.ToString()) (change.``type``.ToString())
        member this.Completion(p: TextDocumentPositionParams): CompletionList option =
            match check (p.textDocument.uri) with 
            | Errors errors -> 
                eprintfn "Check failed, ignored %d errors" (List.length errors)
                None
            | Ok(parseResult, checkResult, _) -> 
                let line = docs.LineContent(p.textDocument.uri, p.position.line)
                let partialName: PartialLongName = QuickParse.GetPartialLongNameEx(line, p.position.character-1)
                eprintfn "Autocompleting %s" (String.concat "." (partialName.QualifyingIdents@[partialName.PartialIdent]))
                let declarations = checkResult.GetDeclarationListInfo(Some parseResult, p.position.line+1, line, partialName) |> Async.RunSynchronously
                Some (convertDeclarations declarations)
        member this.Hover(p: TextDocumentPositionParams): option<Hover> = 
            match check (p.textDocument.uri) with 
            | Errors errors -> 
                eprintfn "Check failed, ignored %d errors" (List.length errors)
                None
            | Ok(parseResult, checkResult, _) -> 
                let line = docs.LineContent(p.textDocument.uri, p.position.line)
                let names = findNamesUnderCursor line p.position.character
                let tips = checkResult.GetToolTipText(p.position.line+1, p.position.character+1, line, names, FSharpTokenTag.Identifier) |> Async.RunSynchronously
                Some(asHover tips)
        member this.ResolveCompletionItem(p: CompletionItem): CompletionItem = TODO()
        member this.SignatureHelp(p: TextDocumentPositionParams): SignatureHelp option = 
            match check (p.textDocument.uri) with 
            | Errors errors -> 
                eprintfn "Check failed, ignored %d errors" (List.length errors)
                None
            | Ok(parseResult, checkResult, _) -> 
                let line = docs.LineContent(p.textDocument.uri, p.position.line)
                match findMethodCallBeforeCursor line p.position.character with 
                | None -> 
                    eprintfn "No method call in line %s" line 
                    None
                | Some endOfMethodName -> 
                    let names = findNamesUnderCursor line (endOfMethodName - 1)
                    eprintfn "Looking for overloads of %s" (String.concat "." names)
                    let overloads = checkResult.GetMethods(p.position.line+1, endOfMethodName, line, Some names) |> Async.RunSynchronously
                    let sigs = Array.map (convertSignature overloads.MethodName) overloads.Methods |> List.ofArray
                    let activeParameter = countCommas line endOfMethodName p.position.character
                    let activeDeclaration = findCompatibleOverload activeParameter overloads.Methods
                    eprintfn "Found %d overloads" overloads.Methods.Length
                    Some({signatures=sigs; activeSignature=activeDeclaration; activeParameter=Some activeParameter})
        member this.GotoDefinition(p: TextDocumentPositionParams): list<Location> = TODO()
        member this.FindReferences(p: ReferenceParams): list<Location> = TODO()
        member this.DocumentHighlight(p: TextDocumentPositionParams): list<DocumentHighlight> = TODO()
        member this.DocumentSymbols(p: DocumentSymbolParams): list<SymbolInformation> = TODO()
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams): list<SymbolInformation> = 
            let openProjects = projects.OpenProjects
            let names = openProjects |> List.map (fun f -> f.ProjectFileName) |> String.concat ", "
            eprintfn "Looking for symbols matching %s in %s" p.query names
            let all = seq {
                for options in openProjects do 
                    let check = checker.ParseAndCheckProject options |> Async.RunSynchronously
                    for e in check.AssemblySignature.Entities do 
                        yield e :> FSharpSymbol
                        for x in e.MembersFunctionsAndValues do
                            yield x :> FSharpSymbol
                        for x in e.UnionCases do
                            yield x :> FSharpSymbol
            }
            all |> Seq.filter (matchesQuery p.query) 
                |> Seq.filter (fun s -> s.DeclarationLocation.IsSome)
                |> Seq.truncate 50 
                |> Seq.map symbolInformation 
                |> List.ofSeq

        member this.CodeActions(p: CodeActionParams): list<Command> = TODO()
        member this.CodeLens(p: CodeLensParams): List<CodeLens> = TODO()
        member this.ResolveCodeLens(p: CodeLens): CodeLens = TODO()
        member this.DocumentLink(p: DocumentLinkParams): list<DocumentLink> = TODO()
        member this.ResolveDocumentLink(p: DocumentLink): DocumentLink = TODO()
        member this.DocumentFormatting(p: DocumentFormattingParams): list<TextEdit> = TODO()
        member this.DocumentRangeFormatting(p: DocumentRangeFormattingParams): list<TextEdit> = TODO()
        member this.DocumentOnTypeFormatting(p: DocumentOnTypeFormattingParams): list<TextEdit> = TODO()
        member this.Rename(p: RenameParams): WorkspaceEdit = TODO()
        member this.ExecuteCommand(p: ExecuteCommandParams): unit = TODO()

[<EntryPoint>]
let main (argv: array<string>): int =
    let read = new BinaryReader(Console.OpenStandardInput())
    let write = new BinaryWriter(Console.OpenStandardOutput())
    let serverFactory = fun client -> Server(client) :> ILanguageServer
    eprintfn "Listening on stdin"
    try 
        LanguageServer.connect serverFactory read write
        0 // return an integer exit code
    with e -> 
        eprintfn "Exception in language server %O" e
        1
