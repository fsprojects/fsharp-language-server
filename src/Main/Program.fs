module Main.Program

open LSP.Log
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open System
open System.IO
open LSP
open LSP.Types
open System.Text.RegularExpressions
open LSP.Json
open LSP.Json.JsonExtensions

let private TODO() = raise (Exception "TODO")

// Convert an F# Compiler Services 'FSharpErrorInfo' to an LSP 'Range'
let private errorAsRange(err: FSharpErrorInfo): Range = 
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
let private asDiagnostic(err: FSharpErrorInfo): Diagnostic = 
    {
        range = errorAsRange(err)
        severity = Some(asDiagnosticSeverity err.Severity)
        code = Some(sprintf "%d: %s" err.ErrorNumber err.Subcategory)
        source = None
        message = err.Message
    }
    
// Some compiler errors have no location in the file and should be displayed at the top of the file
let private hasNoLocation(err: FSharpErrorInfo): bool = 
    err.StartLineAlternate-1 = 0 && 
    err.StartColumn = 0 &&
    err.EndLineAlternate-1 = 0 &&
    err.EndColumn = 0

// A special error message that shows at the top of the file
let private errorAtTop(message: string): Diagnostic =
    {
        range = { start = {line=0; character=0}; ``end`` = {line=0; character=1} }
        severity = Some(DiagnosticSeverity.Error) 
        code = None
        source = None 
        message = message
    }

// Convert a list of F# Compiler Services 'FSharpErrorInfo' to LSP 'Diagnostic'
let private asDiagnostics(errors: FSharpErrorInfo[]): Diagnostic list =
    [ 
        for err in errors do 
            if hasNoLocation(err) then 
                yield errorAtTop(sprintf "%s: %s" err.Subcategory err.Message)
            else
                yield asDiagnostic(err) 
    ]

// Look for a fully qualified name leading up to the cursor
// (exposed for testing)
let findNamesUnderCursor(lineContent: string, character: int): string list = 
    let r = Regex(@"(\w+|``[^`]+``)([\.?](\w+|``[^`]+``))*")
    let ms = r.Matches(lineContent)
    let overlaps(m: Match) = m.Index <= character && character <= m.Index + m.Length 
    let found: Match list = [ for m in ms do if overlaps m then yield m ]
    match found with 
    | [] -> 
        dprintfn "No identifiers at %d in line '%s'" character lineContent
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
        dprintfn "Line %s offset %d matched multiple groups %A" lineContent character multiple 
        []

// Look for a method call like foo.MyMethod() before the cursor
// (exposed for testing)
let findMethodCallBeforeCursor(lineContent: string, cursor: int): int option = 
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
            dprintfn "No signature help in let expression %s" lineContent 
            None 
        else if Regex(@"member[ \w\.]+$").IsMatch(prefix) then 
            dprintfn "No signature help in member expression %s" lineContent 
            None 
        else Some prefix.Length

// Look for an identifier under the cursor and find the end of it
let private findEndOfIdentifierUnderCursor(lineContent: string, cursor: int): int option = 
    let r = Regex(@"\w+|``[^`]+``")
    let ms = r.Matches(lineContent)
    let overlaps(m: Match) = m.Index <= cursor && cursor <= m.Index + m.Length 
    let found: Match list = [ for m in ms do if overlaps m then yield m ]
    match found with 
    | [] -> 
        dprintfn "No identifier at %d in line %s" cursor lineContent
        None
    | m::_ -> 
        Some(m.Index + m.Length)

// Figure out the active parameter by counting ',' characters
let private countCommas(lineContent: string, endOfMethodName: int, cursor: int): int = 
    let mutable count = 0
    for i in endOfMethodName .. (min (cursor-1) lineContent.Length) do 
        if lineContent.[i] = ',' then 
            count <- count + 1
    count

// Convert an F# `FSharpToolTipElement` to an LSP `Hover`
let private asHover(FSharpToolTipText tips): Hover = 
    let convert = 
        [ for t in tips do
            match t with 
            | FSharpToolTipElement.None -> () 
            | FSharpToolTipElement.Group elements -> 
                for e in elements do 
                    yield HighlightedString(e.MainDescription, "fsharp")
            | FSharpToolTipElement.CompositionError err -> 
                yield PlainString(err)]
    {contents=convert; range=None}

// Convert an F# `FSharpToolTipText` to text
let private asDocumentation(FSharpToolTipText tips): string option = 
    match tips with 
    | [FSharpToolTipElement.Group [e]] -> Some e.MainDescription
    | _ -> None // When there are zero or multiple overloads, don't display docs

// Convert an F# `CompletionItemKind` to an LSP `CompletionItemKind`
let private asCompletionItemKind(k: Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind): CompletionItemKind option = 
    match k with 
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Field -> Some(CompletionItemKind.Field)
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Property -> Some(CompletionItemKind.Property)
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Method isExtension -> Some(CompletionItemKind.Method)
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Event -> None
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Argument -> Some(CompletionItemKind.Variable)
    | Microsoft.FSharp.Compiler.SourceCodeServices.CompletionItemKind.Other -> None

// Convert an F# `FSharpDeclarationListItem` to an LSP `CompletionItem`
let private asCompletionItem(i: FSharpDeclarationListItem): CompletionItem = 
    { defaultCompletionItem with 
        label = i.Name 
        kind = asCompletionItemKind i.Kind
        detail = Some i.FullName
        // Stash FullName in data so we can use it later in ResolveCompletionItem
        data = JsonValue.Record [|"FullName", JsonValue.String(i.FullName)|]
    }

// Convert an F# `FSharpDeclarationListInfo` to an LSP `CompletionList`
// Used in rendering autocomplete lists
let private asCompletionList(ds: FSharpDeclarationListInfo): CompletionList = 
    let items = [for i in ds.Items do yield asCompletionItem(i)]
    {isIncomplete=false; items=items}

// Convert an F# `FSharpMethodGroupItemParameter` to an LSP `ParameterInformation`
let private asParameterInformation(p: FSharpMethodGroupItemParameter): ParameterInformation = 
    {
        label = p.ParameterName
        documentation = Some p.Display
    }

// Convert an F# method name + `FSharpMethodGroupItem` to an LSP `SignatureInformation`
// Used in providing signature help after autocompleting
let private asSignatureInformation(methodName: string, s: FSharpMethodGroupItem): SignatureInformation = 
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

// Check if `candidate` contains all the characters of `find`, in-order, case-insensitive
// Matches can be discontinuous if the letters of `find` match the first letters of words in `candidate`
// For example, fb matches FooBar, but it doesn't match Foobar
// (exposed for testing)
let matchesTitleCase(find: string, candidate: string): bool = 
    let mutable i = 0
    let lowerEquals(x, y) = 
        Char.ToLower(x) = Char.ToLower(y)
    let matchNextChar(f) = 
        if i < candidate.Length && lowerEquals(candidate.[i], f) then 
            i <- i + 1 
            true 
        else false 
    let isStartOfWord(i) = 
        0 < i && i < candidate.Length && Char.IsLower(candidate.[i - 1]) && Char.IsUpper(candidate.[i])
    let matchStartOfNextWord(f) = 
        let test(i) = isStartOfWord(i) && lowerEquals(candidate.[i], f)
        while i < candidate.Length && not(test(i)) do
            i <- i + 1 
        test(i)
    let mutable matched = true
    for f in find do 
        matched <- matched && (matchNextChar(f) || matchStartOfNextWord(f))
    matched

// Check if an F# symbol matches a query typed by the user
let private matchesQuery(query: string, candidate: string): bool = 
    matchesTitleCase(query, candidate)

// Find the name of the namespace, type or module that contains `s`
let private containerName(s: FSharpSymbol): string option = 
    if s.FullName = s.DisplayName then 
        None
    else if s.FullName.EndsWith("." + s.DisplayName) then 
        Some(s.FullName.Substring(0, s.FullName.Length - s.DisplayName.Length - 1))
    else 
        Some(s.FullName)

// Convert an F# `Range.pos` to an LSP `Position`
let private asPosition(p: Range.pos): Position = 
    {
        line=p.Line-1
        character=p.Column
    }

// Convert an F# `Range.range` to an LSP `Range`
let private asRange(r: Range.range): Range = 
    {
        start=asPosition r.Start
        ``end``=asPosition r.End
    }

// Convert an F# `Range.range` to an LSP `Location`
let private asLocation(l: Range.range): Location = 
    { 
        uri=Uri("file://" + l.FileName)
        range = asRange l 
    }

// Get the lcation where `s` was declared
let private declarationLocation(s: FSharpSymbol): Location option = 
    match s.DeclarationLocation with 
    | None -> 
        dprintfn "Symbol %s has no declaration" s.FullName 
        None 
    | Some l ->
        Some(asLocation(l))

// Get the location where `s` was used
let private useLocation(s: FSharpSymbolUse): Location = 
    asLocation(s.RangeAlternate)

// Find the first overload in `method` that is compatible with `activeParameter`
// TODO actually consider types
let private findCompatibleOverload(activeParameter: int, methods: FSharpMethodGroupItem[]): int option = 
    let mutable result = -1 
    for i in 0 .. methods.Length - 1 do 
        if result = -1 && (activeParameter = 0 || activeParameter < methods.[i].Parameters.Length) then 
            result <- i 
    if result = -1 then None else Some result

// Convert an F# `FSharpNavigationDeclarationItemKind` to an LSP `SymbolKind`
// `FSharpNavigationDeclarationItemKind` is the level of symbol-type information you get when parsing without typechecking
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

// Convert an F# `FSharpNavigationDeclarationItem` to an LSP `SymbolInformation`
// `FSharpNavigationDeclarationItem` is the parsed AST representation of a symbol without typechecking
// `container` is present when `d` is part of a module or type
let private asSymbolInformation(d: FSharpNavigationDeclarationItem, container: FSharpNavigationDeclarationItem option): SymbolInformation = 
    let declarationName(d: FSharpNavigationDeclarationItem) = d.Name
    {
        name=d.Name 
        kind=asSymbolKind d.Kind 
        location=asLocation d.Range 
        containerName=Option.map declarationName container
    }

// Find all symbols in a parsed AST
let private flattenSymbols(parse: FSharpParseFileResults): (FSharpNavigationDeclarationItem * FSharpNavigationDeclarationItem option) list = 
    [ for d in parse.GetNavigationItems().Declarations do 
        yield d.Declaration, None
        for n in d.Nested do 
            yield n, Some(d.Declaration) ]

type Server(client: ILanguageClient) = 
    let docs = DocumentStore()
    let projects = ProjectManager()
    let checker = FSharpChecker.Create()

    // Get a file from docs, or read it from disk
    let getOrRead(uri: Uri): string option = 
        match docs.GetText(uri) with 
        | Some text -> Some(text)
        | None when File.Exists(uri.LocalPath) -> Some(File.ReadAllText(uri.LocalPath))
        | None -> None

    // Read a specific line from a file
    let lineContent(uri: Uri, targetLine: int): string = 
        let text = getOrRead(uri) |> Option.defaultValue ""
        let reader = new StringReader(text)
        let mutable line = 0
        while line < targetLine && reader.Peek() <> -1 do 
            reader.ReadLine() |> ignore
            line <- line + 1
        if reader.Peek() = -1 then 
            dprintfn "Reached EOF before line %d in file %O" targetLine uri
            "" 
        else 
            reader.ReadLine()

    // Parse a file 
    let parseFile(uri: Uri): Async<Result<FSharpParseFileResults, string>> = 
        async {
            let file = FileInfo(uri.LocalPath)
            match projects.FindProjectOptions(file), getOrRead(uri) with 
            | Error e, _ ->
                return Error(sprintf "Can't find symbols in %s because of error in project options: %s" file.Name e)
            | _, None -> 
                return Error(sprintf "Can't find symbols in non-existant file %s" file.FullName)
            | Ok(projectOptions), Some(sourceText) -> 
                match checker.TryGetRecentCheckResultsForFile(uri.LocalPath, projectOptions) with 
                | Some(parse, _, _) -> 
                    return Ok parse
                | None ->
                    try
                        let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions(projectOptions)
                        let! parse = checker.ParseFile(uri.LocalPath, sourceText, parsingOptions)
                        return Ok(parse)
                    with e -> 
                        return Error(e.Message)
        }
    // Typecheck a file, ignoring caches
    let forceCheckOpenFile(uri: Uri): Async<Result<FSharpParseFileResults * FSharpCheckFileResults, Diagnostic list>> = 
        async {
            let file = FileInfo(uri.LocalPath)
            match projects.FindProjectOptions(file), docs.Get(uri) with 
            | Error(e), _ -> return Error [errorAtTop(e)]
            | _, None -> return Error [errorAtTop(sprintf "No source file %A" uri)]
            | Ok(projectOptions), Some(sourceText, sourceVersion) -> 
                let! force = checker.ParseAndCheckFileInProject(uri.LocalPath, sourceVersion, sourceText, projectOptions)
                match force with 
                | parseResult, FSharpCheckFileAnswer.Aborted -> return Error(asDiagnostics parseResult.Errors)
                | parseResult, FSharpCheckFileAnswer.Succeeded(checkResult) -> return Ok(parseResult, checkResult)
        }
    // Typecheck a file
    let checkOpenFile(uri: Uri): Async<Result<FSharpParseFileResults * FSharpCheckFileResults, Diagnostic list>> = 
        async {
            let file = FileInfo(uri.LocalPath)
            match projects.FindProjectOptions(file), docs.GetVersion(uri) with 
            | Error(e), _ -> return Error [errorAtTop e]
            | _, None -> return Error [errorAtTop (sprintf "No source file %A" uri)]
            | Ok(projectOptions), Some(sourceVersion) -> 
                match checker.TryGetRecentCheckResultsForFile(uri.LocalPath, projectOptions) with 
                | Some(parseResult, checkResult, version) when version = sourceVersion -> 
                    return Ok(parseResult, checkResult)
                | _ -> 
                    return! forceCheckOpenFile(uri)
        }
    // Typecheck a file quickly and a little less accurately
    // If the file has never been checked before, this is the same as `checkOpenFile`
    // If the file has been checked before, the previous check is returned, and a new check is queued
    let quickCheckOpenFile(uri: Uri): Async<Result<FSharpParseFileResults * FSharpCheckFileResults, Diagnostic list>> = 
        async {
            let file = FileInfo(uri.LocalPath)
            match projects.FindProjectOptions(file), docs.Get(uri) with 
            | Error(e), _ -> return Error [errorAtTop e]
            | _, None -> return Error [errorAtTop (sprintf "No source file %A" uri)]
            | Ok(projectOptions), Some(sourceText, sourceVersion) -> 
                match checker.TryGetRecentCheckResultsForFile(uri.LocalPath, projectOptions) with 
                | Some(parseResult, checkResult, version) -> 
                    // Queue a checking operation
                    checker.ParseAndCheckFileInProject(uri.LocalPath, sourceVersion, sourceText, projectOptions) |> ignore
                    // Return the cached check, even if it is out-of-date
                    return Ok(parseResult, checkResult)
                | _ -> 
                    return! forceCheckOpenFile(uri)

        }
    // Check a file and send all errors to the client
    let lint(uri: Uri): Async<unit> = 
        async {
            let! check = forceCheckOpenFile(uri)
            let errors = 
                match check with
                | Error(errors) -> errors
                | Ok(parseResult, checkResult) -> asDiagnostics(parseResult.Errors)@asDiagnostics(checkResult.Errors)
            client.PublishDiagnostics({uri=uri; diagnostics=errors})
        }
    // Find the symbol at a position
    let symbolAt(textDocument: TextDocumentIdentifier, position: Position): Async<FSharpSymbolUse option> = 
        async {
            let! c = checkOpenFile(textDocument.uri)
            match c with 
            | Error(errors) -> 
                dprintfn "Check failed, ignored %d errors" (List.length errors)
                return None
            | Ok(parseResult, checkResult) -> 
                let line = lineContent(textDocument.uri, position.line)
                match findEndOfIdentifierUnderCursor(line, position.character) with 
                | None -> 
                    dprintfn "No identifier at %d in line '%s'" position.character line 
                    return None
                | Some endOfIdentifier -> 
                    dprintfn "Looking for symbol at %d in %s" (endOfIdentifier - 1) line
                    let names = findNamesUnderCursor(line, endOfIdentifier - 1)
                    let dotName = String.concat "." names
                    dprintfn "Looking at symbol %s" dotName
                    let! maybeSymbol = checkResult.GetSymbolUseAtLocation(position.line+1, endOfIdentifier, line, names)
                    if maybeSymbol.IsNone then
                        dprintfn "%s in line '%s' is not a symbol use" dotName line
                    return maybeSymbol
        }

    // Find the exact location of a symbol within a fully-qualified name
    // For example, if we have `let b = Foo.bar`, and we want to find the symbol `bar` in the range `let b = [Foo.bar]`
    let refineRenameRange(s: FSharpSymbol, file: string, range: Range.range): Range = 
        let uri = Uri("file://" + file)
        let line = range.End.Line - 1
        let startColumn = if range.Start.Line - 1 < line then 0 else range.Start.Column
        let endColumn = range.End.Column
        let lineText = lineContent(uri, line )
        let find = lineText.LastIndexOf(s.DisplayName, endColumn, endColumn - startColumn)
        if find = -1 then
            dprintfn "Couldn't find '%s' in line '%s'" s.DisplayName lineText 
            asRange range
        else 
            {
                start={line=line; character=find}
                ``end``={line=line; character=find + s.DisplayName.Length}
            }
    // Rename one usage of a symbol
    let renameTo(newName: string, file: string, usages: FSharpSymbolUse seq): TextDocumentEdit = 
        let uri = Uri("file://" + file)
        let version = docs.GetVersion(uri) |> Option.defaultValue 0
        let edits = [
            for u in usages do 
                let range = refineRenameRange(u.Symbol, u.FileName, u.RangeAlternate)
                yield {range=range; newText=newName} ]
        {textDocument={uri=uri; version=version}; edits=edits}

    // Determine if one .fs file is an ancestor of another, so that we can correctly invalidate descendents when the user saves changes
    let isAncestorInProject(project: FSharpProjectOptions, parent: FileInfo, child: FileInfo) = 
        match Array.tryFindIndex ((=) parent.FullName) project.SourceFiles, Array.tryFindIndex ((=) child.FullName) project.SourceFiles with 
        | Some(iParent), Some(iChild) -> iParent < iChild 
        | _, _ -> false
    let isAncestorProject(parentProject: FSharpProjectOptions, childProject: FSharpProjectOptions) = 
        let rec traverse (p: FSharpProjectOptions): bool = 
            let traverseNext(_, next) = traverse next
            let stop = p.ProjectFileName = parentProject.ProjectFileName
            stop || Array.exists traverseNext p.ReferencedProjects
        parentProject.ProjectFileName <> childProject.ProjectFileName && traverse childProject
    let isAncestor(parent: Uri, child: Uri): bool = 
        let parentFile = FileInfo(parent.LocalPath)
        let childFile = FileInfo(child.LocalPath)
        match projects.FindProjectOptions(parentFile), projects.FindProjectOptions(childFile) with 
        | Ok(parentProject), Ok(childProject) -> 
            isAncestorInProject(parentProject, parentFile, childFile) || isAncestorProject(parentProject, childProject)
        | _, _ -> false
    
    // List of all .fs files that depend on `uri`
    // These are the files that are invalidated when the user saves changes to `uri`
    let openDescendentFiles(uri: Uri): Uri list = 
        [for candidate in docs.OpenFiles() do 
            if isAncestor(uri, candidate) then 
                yield candidate ]

    let printProjectNames(openProjects: FSharpProjectOptions list): string = 
        let projectName(f: FSharpProjectOptions) = f.ProjectFileName
        let names = List.map projectName openProjects
        String.concat ", " names

    // Quickly check if a file *might* contain a symbol matching query
    let symbolPattern = Regex(@"\w+")
    let maybeMatchesQuery(query: string, uri: Uri): string option = 
        match getOrRead(uri) with 
        | None -> None 
        | Some text -> 
            let matches = symbolPattern.Matches(text)
            let test(m: Match) = matchesQuery(query, m.Value)
            if Seq.exists test matches then 
                Some text 
            else 
                None
    let exactlyMatches(findSymbol: string, uri: Uri): string option =
        match getOrRead(uri) with 
        | None -> None 
        | Some text -> 
            let matches = symbolPattern.Matches(text)
            let test(m: Match) = m.Value = findSymbol
            if Seq.exists test matches then 
                Some text 
            else 
                None

    // Find all uses of a symbol, across all open projects
    let findAllSymbolUses(symbol: FSharpSymbol): Async<List<FSharpSymbolUse>> = 
        // TODO only search 1 file for local variables and private symbols
        async {
            dprintfn "Looking for references to %s in %s" symbol.FullName (printProjectNames(projects.OpenProjects))
            let all = System.Collections.Generic.List<FSharpSymbolUse>()
            for projectOptions in projects.OpenProjects do 
                for sourceFile in projectOptions.SourceFiles do 
                    let uri = Uri("file://" + sourceFile)
                    match exactlyMatches(symbol.DisplayName, uri) with 
                    | None -> () 
                    | Some sourceText ->
                        try
                            dprintfn "Checking %s for references to %s" sourceFile symbol.DisplayName // TODO show progress bar
                            let sourceVersion = docs.GetVersion(uri) |> Option.defaultValue 0
                            let! _, maybeCheck = checker.ParseAndCheckFileInProject(uri.LocalPath, sourceVersion, sourceText, projectOptions)
                            match maybeCheck with 
                            | FSharpCheckFileAnswer.Aborted -> ()
                            | FSharpCheckFileAnswer.Succeeded(check) -> 
                                let! uses = check.GetUsesOfSymbolInFile(symbol)
                                for u in uses do 
                                    all.Add(u)
                        with e -> 
                            dprintfn "Error checking %s: %s" sourceFile e.Message
            return List.ofSeq(all)
        }

    // Remember the last completion list for ResolveCompletionItem
    let mutable lastCompletion: FSharpDeclarationListInfo option = None 

    interface ILanguageServer with 
        member this.Initialize(p: InitializeParams) =
            async {
                match p.rootUri with 
                | Some root -> 
                    dprintfn "Adding workspace root %s ~ %s" root.AbsoluteUri root.LocalPath
                    projects.AddWorkspaceRoot(DirectoryInfo(root.LocalPath)) 
                | _ -> ()
                return { 
                    capabilities = 
                        { defaultServerCapabilities with 
                            hoverProvider = true
                            completionProvider = Some({resolveProvider=true; triggerCharacters=['.']})
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
        member this.Initialized(): Async<unit> = 
            async { () }
        member this.Shutdown(): Async<unit> = 
            async { () }
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams): Async<unit> =
            async {
                dprintfn "New configuration %s" (p.ToString())
            }
        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams): Async<unit> = 
            async {
                docs.Open(p)
                do! lint(p.textDocument.uri)
            }
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams): Async<unit> = 
            async {
                docs.Change(p)
            }
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams): Async<unit> = TODO()
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): Async<TextEdit list> = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams): Async<unit> = 
            async {
                let files = p.textDocument.uri::openDescendentFiles(p.textDocument.uri)
                let uriFileName(uri:Uri) = FileInfo(uri.LocalPath).Name
                let names = List.map uriFileName files
                dprintfn "Lint %s" (String.Join(", ", names))
                for f in files do 
                    do! lint(f)
            }
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams): Async<unit> = 
            async {
                docs.Close(p)
            }
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): Async<unit> = 
            async {
                for change in p.changes do 
                    let file = FileInfo(change.uri.LocalPath)
                    dprintfn "Watched file %s %O" file.FullName change.``type``
                    if file.Name.EndsWith(".fsproj") then 
                        match change.``type`` with 
                        | FileChangeType.Created ->
                            projects.NewProjectFile(file)
                        | FileChangeType.Changed ->
                            projects.UpdateProjectFile(file)
                        | FileChangeType.Deleted ->
                            projects.DeleteProjectFile(file)
                    elif file.Name = "project.assets.json" then 
                        projects.UpdateAssetsJson(file)
                // Re-lint all open files
                // In theory we could optimize this by only re-linting descendents of changed projects, 
                // but in practice that will make little difference
                for f in docs.OpenFiles() do 
                    do! lint(f) 
            }
        member this.Completion(p: TextDocumentPositionParams): Async<CompletionList option> =
            async {
                dprintfn "Autocompleting at %s(%d,%d)" p.textDocument.uri.LocalPath p.position.line p.position.character
                let! c = quickCheckOpenFile(p.textDocument.uri)
                dprintfn "Finished typecheck, looking for completions..."
                match c with 
                | Error errors -> 
                    dprintfn "Check failed, ignored %d errors" (List.length(errors))
                    return None
                | Ok(parseResult, checkResult) -> 
                    let line = lineContent(p.textDocument.uri, p.position.line)
                    let partialName: PartialLongName = QuickParse.GetPartialLongNameEx(line, p.position.character-1)
                    let nameParts = partialName.QualifyingIdents@[partialName.PartialIdent]
                    dprintfn "Autocompleting %s" (String.concat "." nameParts)
                    let! declarations = checkResult.GetDeclarationListInfo(Some parseResult, p.position.line+1, line, partialName)
                    lastCompletion <- Some declarations 
                    dprintfn "Found %d completions" declarations.Items.Length
                    return Some(asCompletionList(declarations))
            }
        member this.Hover(p: TextDocumentPositionParams): Async<Hover option> = 
            async {
                let! c = checkOpenFile(p.textDocument.uri)
                match c with 
                | Error errors -> 
                    dprintfn "Check failed, ignored %d errors" (List.length(errors))
                    return None
                | Ok(parseResult, checkResult) -> 
                    let line = lineContent(p.textDocument.uri, p.position.line)
                    let names = findNamesUnderCursor(line, p.position.character)
                    let! tips = checkResult.GetToolTipText(p.position.line+1, p.position.character+1, line, names, FSharpTokenTag.Identifier)
                    return Some(asHover(tips))
            }
        // Add documentation to a completion item
        // Generating documentation is an expensive step, so we want to defer it until the user is actually looking at it
        member this.ResolveCompletionItem(p: CompletionItem): Async<CompletionItem> = 
            async {
                let mutable result = p
                if lastCompletion.IsSome then 
                    for candidate in lastCompletion.Value.Items do 
                        if candidate.FullName = p.data?FullName.AsString() then 
                            dprintfn "Resolve description for %s" candidate.FullName
                            result <- {p with documentation=asDocumentation(candidate.DescriptionText)}
                return result
            }
        member this.SignatureHelp(p: TextDocumentPositionParams): Async<SignatureHelp option> = 
            async {
                let! c = quickCheckOpenFile(p.textDocument.uri)
                match c with 
                | Error errors -> 
                    dprintfn "Check failed, ignored %d errors" (List.length(errors))
                    return None
                | Ok(parseResult, checkResult) -> 
                    let line = lineContent(p.textDocument.uri, p.position.line)
                    match findMethodCallBeforeCursor(line, p.position.character) with 
                    | None -> 
                        dprintfn "No method call in line %s" line 
                        return None
                    | Some endOfMethodName -> 
                        let names = findNamesUnderCursor(line, endOfMethodName - 1)
                        dprintfn "Looking for overloads of %s" (String.concat "." names)
                        let! overloads = checkResult.GetMethods(p.position.line+1, endOfMethodName, line, Some names)
                        let signature(i: FSharpMethodGroupItem) = asSignatureInformation(overloads.MethodName, i)
                        let sigs = Array.map signature overloads.Methods |> List.ofArray
                        let activeParameter = countCommas(line, endOfMethodName, p.position.character)
                        let activeDeclaration = findCompatibleOverload(activeParameter, overloads.Methods)
                        dprintfn "Found %d overloads" overloads.Methods.Length
                        return Some({signatures=sigs; activeSignature=activeDeclaration; activeParameter=Some activeParameter})
            }
        member this.GotoDefinition(p: TextDocumentPositionParams): Async<Location list> = 
            async {
                let! maybeSymbol = symbolAt(p.textDocument, p.position)
                match maybeSymbol with 
                | None -> return []
                | Some s -> return declarationLocation s.Symbol |> Option.toList
            }
        member this.FindReferences(p: ReferenceParams): Async<Location list> = 
            async {
                let! maybeSymbol = symbolAt(p.textDocument, p.position)
                match maybeSymbol with 
                | None -> return [] 
                | Some s -> 
                    let! uses = findAllSymbolUses(s.Symbol)
                    return List.map useLocation uses
            }
        member this.DocumentHighlight(p: TextDocumentPositionParams): Async<DocumentHighlight list> = TODO()
        member this.DocumentSymbols(p: DocumentSymbolParams): Async<SymbolInformation list> =
            async {
                let! maybeParse = parseFile(p.textDocument.uri)
                match maybeParse with 
                | Error e -> 
                    dprintfn "%s" e 
                    return []
                | Ok parse ->
                    let flat = flattenSymbols(parse)
                    return List.map asSymbolInformation flat
            }
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams): Async<SymbolInformation list> = 
            async {
                dprintfn "Looking for symbols matching %s in %s" p.query (printProjectNames(projects.OpenProjects))
                // Read open projects until we find at least 50 symbols that match query
                let all = System.Collections.Generic.List<SymbolInformation>()
                for projectOptions in projects.OpenProjects do 
                    for sourceFile in projectOptions.SourceFiles do 
                        if all.Count < 50 then 
                            let uri = Uri("file://" + sourceFile)
                            match maybeMatchesQuery(p.query, uri) with 
                            | None -> () 
                            | Some sourceText ->
                                try
                                    let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions(projectOptions)
                                    let! parse = checker.ParseFile(uri.LocalPath, sourceText, parsingOptions)
                                    for declaration, container in flattenSymbols(parse) do 
                                        if matchesQuery(p.query, declaration.Name) then 
                                            all.Add(asSymbolInformation(declaration, container))
                                with e -> 
                                    dprintfn "Error parsing %s: %s" sourceFile e.Message
                return List.ofSeq(all)
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
                let! maybeSymbol = symbolAt(p.textDocument, p.position)
                match maybeSymbol with 
                | None -> return {documentChanges=[]}
                | Some s -> 
                    let! uses = findAllSymbolUses(s.Symbol)
                    let byFile = List.groupBy (fun (usage:FSharpSymbolUse) -> usage.FileName) uses
                    let fileNames = List.map fst byFile
                    dprintfn "Renaming %s to %s in %s" s.Symbol.FullName p.newName (String.concat ", " fileNames)
                    let renames = List.map (fun (file, uses) -> renameTo(p.newName, file, uses)) byFile
                    return {documentChanges=List.ofSeq(renames)}
            }
        member this.ExecuteCommand(p: ExecuteCommandParams): Async<unit> = TODO()
        member this.DidChangeWorkspaceFolders(p: DidChangeWorkspaceFoldersParams): Async<unit> = 
            async {
                for root in p.event.added do 
                    projects.AddWorkspaceRoot(DirectoryInfo(root.uri.LocalPath))
                // TODO removed
            }

[<EntryPoint>]
let main(argv: array<string>): int =
    let read = new BinaryReader(Console.OpenStandardInput())
    let write = new BinaryWriter(Console.OpenStandardOutput())
    let serverFactory(client) = Server(client) :> ILanguageServer
    dprintfn "Listening on stdin"
    try 
        LanguageServer.connect(serverFactory, read, write)
        0 // return an integer exit code
    with e -> 
        dprintfn "Exception in language server %O" e
        1
