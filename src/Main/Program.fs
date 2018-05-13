module Main.Program

open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open System
open System.IO
open LSP
open LSP.Types
open System.Text.RegularExpressions
open Log

let private TODO() = raise (Exception "TODO")

// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Range'
let private errorAsRange (err: FSharpErrorInfo): Range = 
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
        range = errorAsRange(err)
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
        log "%s" message 
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
        log "No identifiers at %d in line %s" character lineContent
        [] 
    | single::[] -> 
        let r = Regex(@"(\w+|``[^`]+``)")
        let ms = r.Matches(single.Value)
        let result = [ for m in ms do 
                            if single.Index + m.Index <= character then 
                                if m.Value.StartsWith("``") then
                                    yield m.Value.Substring(2, m.Value.Length - 4)
                                else
                                    yield m.Value ]
        result
    | multiple -> 
        log "Line %s offset %d matched multiple groups %A" lineContent character multiple 
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
            log "No signature help in let expression %s" lineContent 
            None 
        else if Regex(@"member[ \w\.]+$").IsMatch(prefix) then 
            log "No signature help in member expression %s" lineContent 
            None 
        else Some prefix.Length

let findEndOfIdentifierUnderCursor (lineContent: string) (cursor: int): int option = 
    let r = Regex(@"\w+|``[^`]+``")
    let ms = r.Matches(lineContent)
    let overlaps (m: Match) = m.Index <= cursor && cursor <= m.Index + m.Length 
    let found: Match list = [ for m in ms do if overlaps m then yield m ]
    match found with 
    | [] -> 
        log "No identifier at %d in line %s" cursor lineContent
        None
    | m::_ -> 
        Some(m.Index + m.Length)


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
                log "Tooltip error %s" err]
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
                    log "Can't render documentation %A" s.Description 
                    None 
    let parameterName (p: FSharpMethodGroupItemParameter) = p.ParameterName
    let parameterNames = Array.map parameterName s.Parameters
    {
        label = sprintf "%s(%s)" methodName (String.concat ", " parameterNames) 
        documentation = doc 
        parameters = Array.map convertParameter s.Parameters |> List.ofArray
    }

// Lazily all symbols in a file or project
let rec private allSymbols (es: FSharpEntity seq) = 
    seq {
        for e in es do 
            yield e :> FSharpSymbol
            for x in e.MembersFunctionsAndValues do
                yield x :> FSharpSymbol
            for x in e.UnionCases do
                yield x :> FSharpSymbol
            for x in e.FSharpFields do
                yield x :> FSharpSymbol
            yield! allSymbols e.NestedEntities
    }

// Check if candidate contains all the characters of find, in-order, case-insensitive
// candidate is allowed to have other characters in between, as long as it contains all of find in-order
let private containsChars (find: string) (candidate: string): bool = 
    let mutable iFind = 0
    for c in candidate do 
        if iFind < find.Length && c = find.[iFind] then iFind <- iFind + 1
    iFind = find.Length

let private matchesQuery (query: string) (candidate: FSharpSymbol): bool = 
    containsChars (query.ToLower()) (candidate.DisplayName.ToLower())

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
    {line=p.Line-1; character=p.Column}

let private asRange (r: Range.range): Range = 
    {
        start=asPosition r.Start
        ``end``=asPosition r.End
    }

let private declarationLocation (s: FSharpSymbol): Location option = 
    match s.DeclarationLocation with 
    | None -> 
        log "Symbol %s has no declaration" s.FullName 
        None 
    | Some l ->
        let l = s.DeclarationLocation.Value
        let uri = Uri("file://" + l.FileName)
        Some({ uri=uri; range = asRange l })

let private useLocation (s: FSharpSymbolUse): Location = 
    let uri = Uri("file://" + s.FileName)
    { uri=uri; range = asRange s.RangeAlternate }

let private symbolInformation (s: FSharpSymbol): SymbolInformation = 
    {
        name = s.DisplayName
        containerName = containerName s
        kind = symbolKind(s)
        location = declarationLocation(s).Value
    }

let private symbolIsInFile (file: string) (s: FSharpSymbol): bool = 
    match s.DeclarationLocation with 
    | Some l -> l.FileName = file 
    | None -> false

// TODO actually consider types
let private findCompatibleOverload (activeParameter: int) (methods: FSharpMethodGroupItem[]): int option = 
    let mutable result = -1 
    for i in 0 .. methods.Length - 1 do 
        if result = -1 && (activeParameter = 0 || activeParameter < methods.[i].Parameters.Length) then 
            result <- i 
    if result = -1 then None else Some result

type private FindFile = {
    sourcePath: string
    sourceVersion: int
    sourceText: string
    projectOptions: FSharpProjectOptions 
}

type private CheckFile = 
    | Errors of Diagnostic list
    | GoodFile of parseResult: FSharpParseFileResults * checkResult: FSharpCheckFileResults * errors: Diagnostic list

type Server(client: ILanguageClient) = 
    let docs = DocumentStore()
    let projects = ProjectManager()
    let checker = FSharpChecker.Create()
    // Find a file and its .fsproj context
    let find (uri: Uri): Result<FindFile, string> = 
        let sourcePath = uri.AbsolutePath.ToString()
        let source = docs.Get uri
        let projectOptions = projects.FindProjectOptions(FileInfo(uri.AbsolutePath))
        match source, projectOptions with 
        | None, _ -> Error(sprintf "No source file %O" uri)
        | _, Error m -> Error m
        | Some(sourceText, sourceVersion), Ok projectOptions -> 
            let found = 
                { 
                    sourcePath=sourcePath
                    sourceVersion=sourceVersion
                    sourceText=sourceText
                    projectOptions=projectOptions 
                }
            Ok found
    // Find a file and check it
    let check (uri: Uri): Async<CheckFile> = 
        async {
            match find uri with 
            | Error m -> return Errors [errorAtTop m]
            | Ok f -> 
                let! parseResult, checkAnswer = checker.ParseAndCheckFileInProject(f.sourcePath, f.sourceVersion, f.sourceText, f.projectOptions)
                let parseErrors = convertDiagnostics parseResult.Errors
                match checkAnswer with 
                | FSharpCheckFileAnswer.Aborted -> 
                    log "Aborted checking %s" f.sourcePath 
                    return Errors parseErrors
                | FSharpCheckFileAnswer.Succeeded checkResult -> 
                    let checkErrors = convertDiagnostics checkResult.Errors 
                    let allErrors = parseErrors@checkErrors 
                    return GoodFile(parseResult, checkResult, allErrors)
        }
    // Check a file and send all errors to the client
    let lint (uri: Uri): Async<unit> = 
        async {
            let! c = check uri
            match c with 
            | Errors errors -> client.PublishDiagnostics({uri=uri; diagnostics=errors})
            | GoodFile(parseResult, checkResult, errors) -> client.PublishDiagnostics({uri=uri; diagnostics=errors})
        }
    // Find the symbol at a position
    let symbolAt (textDocument: TextDocumentIdentifier) (position: Position): Async<FSharpSymbolUse option> = 
        async {
            let! c = check (textDocument.uri)
            match c with 
            | Errors errors -> 
                log "Check failed, ignored %d errors" (List.length errors)
                return None
            | GoodFile(parseResult, checkResult, _) -> 
                let line = docs.LineContent(textDocument.uri, position.line)
                match findEndOfIdentifierUnderCursor line position.character with 
                | None -> 
                    log "No identifier at %d in line '%s'" position.character line 
                    return None
                | Some endOfIdentifier -> 
                    log "Looking for symbol at %d in %s" (endOfIdentifier - 1) line
                    let names = findNamesUnderCursor line (endOfIdentifier - 1)
                    let dotName = String.concat "." names
                    log "Looking at symbol %s" dotName
                    let! maybeSymbol = checkResult.GetSymbolUseAtLocation(position.line+1, endOfIdentifier, line, names)
                    if maybeSymbol.IsNone then
                        log "%s in line '%s' is not a symbol use" dotName line
                    return maybeSymbol
        }
    // Rename one usage of a symbol
    let renameTo (newName: string) (file: string, usages: FSharpSymbolUse seq): TextDocumentEdit = 
        let uri = Uri("file://" + file)
        let version = docs.GetVersion(uri) |> Option.defaultValue 0
        let edits = [
            for u in usages do 
                let range = asRange u.RangeAlternate 
                yield {range=range; newText=newName} ]
        {textDocument={uri=uri; version=version}; edits=edits}

    interface ILanguageServer with 
        member this.Initialize(p: InitializeParams) =
            async {
                match p.rootUri with 
                | Some root -> projects.AddWorkspaceRoot (DirectoryInfo(root.AbsolutePath)) 
                | _ -> ()
                return { 
                    capabilities = 
                        { defaultServerCapabilities with 
                            hoverProvider = true
                            completionProvider = Some({resolveProvider=false; triggerCharacters=['.']})
                            signatureHelpProvider = Some({triggerCharacters=['('; ',']})
                            documentSymbolProvider = true
                            workspaceSymbolProvider = true
                            definitionProvider = true
                            referencesProvider = true
                            renameProvider = true
                            textDocumentSync = 
                                { defaultTextDocumentSyncOptions with 
                                    openClose = true 
                                    save = Some({ includeText = false })
                                    change = TextDocumentSyncKind.Incremental 
                                } 
                        }
                }
            }
        member this.Initialized(): unit = 
            ()
        member this.Shutdown(): unit = 
            ()
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams): unit =
            log "New configuration %s" (p.ToString())
        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams): unit = 
            docs.Open p
            lint p.textDocument.uri |> Async.RunSynchronously
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams): unit = 
            docs.Change p
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams): unit = TODO()
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): Async<TextEdit list> = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams): unit = 
            lint p.textDocument.uri |> Async.RunSynchronously
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams): unit = 
            docs.Close p
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): unit = 
            for change in p.changes do 
                let file = FileInfo(change.uri.AbsolutePath)
                log "Watched file %s %O" file.FullName change.``type``
                if file.Name.EndsWith(".fsproj") then 
                    match change.``type`` with 
                    | FileChangeType.Created ->
                        projects.NewProjectFile file
                    | FileChangeType.Changed ->
                        projects.UpdateProjectFile file
                    | FileChangeType.Deleted ->
                        projects.DeleteProjectFile file
                elif file.Name = "project.assets.json" then 
                    projects.UpdateAssetsJson file
        member this.Completion(p: TextDocumentPositionParams): Async<CompletionList option> =
            async {
                log "Autocompleting at %s(%d,%d)" p.textDocument.uri.AbsolutePath p.position.line p.position.character
                let! c = check p.textDocument.uri
                log "Finished typecheck, looking for completions..."
                match c with 
                | Errors errors -> 
                    log "Check failed, ignored %d errors" (List.length errors)
                    return None
                | GoodFile(parseResult, checkResult, _) -> 
                    let line = docs.LineContent(p.textDocument.uri, p.position.line)
                    let partialName: PartialLongName = QuickParse.GetPartialLongNameEx(line, p.position.character-1)
                    log "Autocompleting %s" (String.concat "." (partialName.QualifyingIdents@[partialName.PartialIdent]))
                    let! declarations = checkResult.GetDeclarationListInfo(Some parseResult, p.position.line+1, line, partialName)
                    log "Found %d completions" declarations.Items.Length
                    return Some (convertDeclarations declarations)
            }
        member this.Hover(p: TextDocumentPositionParams): Async<Hover option> = 
            async {
                let! c = check p.textDocument.uri
                match c with 
                | Errors errors -> 
                    log "Check failed, ignored %d errors" (List.length errors)
                    return None
                | GoodFile(parseResult, checkResult, _) -> 
                    let line = docs.LineContent(p.textDocument.uri, p.position.line)
                    let names = findNamesUnderCursor line p.position.character
                    let! tips = checkResult.GetToolTipText(p.position.line+1, p.position.character+1, line, names, FSharpTokenTag.Identifier)
                    return Some(asHover tips)
            }
        member this.ResolveCompletionItem(p: CompletionItem): Async<CompletionItem> = TODO()
        member this.SignatureHelp(p: TextDocumentPositionParams): Async<SignatureHelp option> = 
            async {
                let! c = check p.textDocument.uri
                match c with 
                | Errors errors -> 
                    log "Check failed, ignored %d errors" (List.length errors)
                    return None
                | GoodFile(parseResult, checkResult, _) -> 
                    let line = docs.LineContent(p.textDocument.uri, p.position.line)
                    match findMethodCallBeforeCursor line p.position.character with 
                    | None -> 
                        log "No method call in line %s" line 
                        return None
                    | Some endOfMethodName -> 
                        let names = findNamesUnderCursor line (endOfMethodName - 1)
                        log "Looking for overloads of %s" (String.concat "." names)
                        let! overloads = checkResult.GetMethods(p.position.line+1, endOfMethodName, line, Some names)
                        let sigs = Array.map (convertSignature overloads.MethodName) overloads.Methods |> List.ofArray
                        let activeParameter = countCommas line endOfMethodName p.position.character
                        let activeDeclaration = findCompatibleOverload activeParameter overloads.Methods
                        log "Found %d overloads" overloads.Methods.Length
                        return Some({signatures=sigs; activeSignature=activeDeclaration; activeParameter=Some activeParameter})
            }
        member this.GotoDefinition(p: TextDocumentPositionParams): Async<Location list> = 
            async {
                let! maybeSymbol = symbolAt p.textDocument p.position
                match maybeSymbol with 
                | None -> return []
                | Some s -> return declarationLocation s.Symbol |> Option.toList
            }
        member this.FindReferences(p: ReferenceParams): Async<Location list> = 
            async {
                let! maybeSymbol = symbolAt p.textDocument p.position
                match maybeSymbol with 
                | None -> return [] 
                | Some s -> 
                    let openProjects = projects.OpenProjects
                    let names = openProjects |> List.map (fun f -> f.ProjectFileName) |> String.concat ", "
                    log "Looking for references to %s in %s" s.Symbol.FullName names
                    let all = System.Collections.Generic.List<Location>()
                    for options in openProjects do 
                        let! check = checker.ParseAndCheckProject options
                        let! uses = check.GetUsesOfSymbol(s.Symbol)
                        for u in uses do 
                            all.Add(useLocation u)
                    return List.ofSeq all
            }
        member this.DocumentHighlight(p: TextDocumentPositionParams): Async<DocumentHighlight list> = TODO()
        member this.DocumentSymbols(p: DocumentSymbolParams): Async<SymbolInformation list> =
            async {
                let! c = check (p.textDocument.uri)
                match c with 
                | Errors errors -> 
                    log "Check failed, ignored %d errors" (List.length errors)
                    return []
                | GoodFile(parseResult, checkResult, _) -> 
                    log "Looking for symbols in %s" parseResult.FileName
                    let all = allSymbols checkResult.PartialAssemblySignature.Entities
                    return all 
                        |> Seq.filter (symbolIsInFile parseResult.FileName)
                        |> Seq.map symbolInformation 
                        |> List.ofSeq
            }
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams): Async<SymbolInformation list> = 
            async {
                // TODO consider just parsing all files and using GetNavigationItems
                let openProjects = projects.OpenProjects
                let names = openProjects |> List.map (fun f -> f.ProjectFileName) |> String.concat ", "
                log "Looking for symbols matching %s in %s" p.query names
                // Read open projects until we find at least 50 symbols that match query
                let all = System.Collections.Generic.List<SymbolInformation>()
                for options in openProjects do 
                    if all.Count < 50 then 
                        let! c = checker.ParseAndCheckProject options
                        for s in allSymbols c.AssemblySignature.Entities do 
                            if matchesQuery p.query s && s.DeclarationLocation.IsSome then 
                                all.Add (symbolInformation s)
                return List.ofSeq all
            }
        member this.CodeActions(p: CodeActionParams): Async<Command list> = TODO()
        member this.CodeLens(p: CodeLensParams): Async<List<CodeLens>> = TODO()
        member this.ResolveCodeLens(p: CodeLens): Async<CodeLens> = TODO()
        member this.DocumentLink(p: DocumentLinkParams): Async<DocumentLink list> = TODO()
        member this.ResolveDocumentLink(p: DocumentLink): Async<DocumentLink> = TODO()
        member this.DocumentFormatting(p: DocumentFormattingParams): Async<TextEdit list> = TODO()
        member this.DocumentRangeFormatting(p: DocumentRangeFormattingParams): Async<TextEdit list> = TODO()
        member this.DocumentOnTypeFormatting(p: DocumentOnTypeFormattingParams): Async<TextEdit list> = TODO()
        member this.Rename(p: RenameParams): Async<WorkspaceEdit> =
            async {
                let! maybeSymbol = symbolAt p.textDocument p.position
                match maybeSymbol with 
                | None -> return {documentChanges=[]}
                | Some s -> 
                    let openProjects = projects.OpenProjects
                    let names = openProjects |> List.map (fun f -> f.ProjectFileName) |> String.concat ", "
                    log "Renaming %s to %s in %s" s.Symbol.FullName p.newName names
                    let all = System.Collections.Generic.List<FSharpSymbolUse>()
                    for options in openProjects do 
                        let! check = checker.ParseAndCheckProject options
                        let! usages = check.GetUsesOfSymbol(s.Symbol)
                        for u in usages do 
                            all.Add u
                    let edits = all |> Seq.groupBy (fun usage -> usage.FileName)
                                    |> Seq.map (renameTo p.newName)
                                    |> List.ofSeq
                    return {documentChanges=edits}
            }
        member this.ExecuteCommand(p: ExecuteCommandParams): Async<unit> = TODO()
        member this.DidChangeWorkspaceFolders(p: DidChangeWorkspaceFoldersParams): unit = 
            for root in p.event.added do 
                projects.AddWorkspaceRoot(DirectoryInfo(root.uri.AbsolutePath))
            // TODO removed

[<EntryPoint>]
let main (argv: array<string>): int =
    let read = new BinaryReader(Console.OpenStandardInput())
    let write = new BinaryWriter(Console.OpenStandardOutput())
    let serverFactory = fun client -> Server(client) :> ILanguageServer
    log "Listening on stdin"
    try 
        LanguageServer.connect serverFactory read write
        0 // return an integer exit code
    with e -> 
        log "Exception in language server %O" e
        1
