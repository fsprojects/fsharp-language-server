module FSharpLanguageServer.Program

open LSP.Log
open FSharp.Compiler
open FSharp.Compiler.Text
open FSharp.Compiler.SourceCodeServices
open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open LSP
open LSP.Types
open FSharp.Data
open FSharp.Data.JsonExtensions
open Conversions

let private TODO() = raise (Exception "TODO")

/// Look for a method call like foo.MyMethod() before the cursor
/// (exposed for testing)
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

/// Figure out the active parameter by counting ',' characters
let private countCommas(lineContent: string, endOfMethodName: int, cursor: int): int =
    let mutable count = 0
    for i in endOfMethodName .. (min (cursor-1) lineContent.Length) do
        if lineContent.[i] = ',' then
            count <- count + 1
    count

/// Check if `candidate` contains all the characters of `find`, in-order, case-insensitive
/// Matches can be discontinuous if the letters of `find` match the first letters of words in `candidate`
/// For example, fb matches FooBar, but it doesn't match Foobar
/// (exposed for testing)
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
        0 <= i && i < candidate.Length && Char.IsUpper(candidate.[i])
    let matchStartOfNextWord(f) =
        let test(i) = isStartOfWord(i) && lowerEquals(candidate.[i], f)
        while i < candidate.Length && not(test(i)) do
            i <- i + 1
        test(i)
    let mutable matched = true
    for f in find do
        matched <- matched && (matchNextChar(f) || matchStartOfNextWord(f))
    matched

/// Check if an F# symbol matches a query typed by the user
let private matchesQuery(query: string, candidate: string): bool =
    matchesTitleCase(query, candidate)

/// Find the first overload in `method` that is compatible with `activeParameter`
// TODO actually consider types
let private findCompatibleOverload(activeParameter: int, methods: FSharpMethodGroupItem[]): int option =
    let mutable result = -1
    for i in 0 .. methods.Length - 1 do
        if result = -1 && (activeParameter = 0 || activeParameter < methods.[i].Parameters.Length) then
            result <- i
    if result = -1 then None else Some result

/// Find searchable declarations
let private findDeclarations(parse: FSharpParseFileResults) =
    let items =
        match parse.ParseTree with
        | Some(SyntaxTree.ParsedInput.SigFile(SyntaxTree.ParsedSigFileInput(_, _, _, _, modules))) ->
            Navigation.getNavigationFromSigFile(modules).Declarations
        | Some(SyntaxTree.ParsedInput.ImplFile(SyntaxTree.ParsedImplFileInput(_, _, _, _, _, modules, _))) ->
            Navigation.getNavigationFromImplFile(modules).Declarations
        | _ -> [||]
    [ for i in items do
        yield i.Declaration, None
        for n in i.Nested do
            yield n, Some(i.Declaration) ]

let private findSignatureDeclarations(parse: FSharpParseFileResults) =
    match parse.ParseTree with
    | Some(SyntaxTree.ParsedInput.SigFile(SyntaxTree.ParsedSigFileInput(_, _, _, _, modules))) ->
        let items = Navigation.getNavigationFromSigFile(modules)
        [ for i in items.Declarations do
            for n in i.Nested do
                yield [i.Declaration.Name; n.Name], n.Range ]
    | _ -> []

let private findSignatureImplementation(parse: FSharpParseFileResults, name: string list) =
    match parse.ParseTree with
    | Some(SyntaxTree.ParsedInput.ImplFile(SyntaxTree.ParsedImplFileInput(_, _, _, _, _, modules, _))) ->
        let items = Navigation.getNavigationFromImplFile(modules)
        [ for i in items.Declarations do
            for n in i.Nested do
                if [i.Declaration.Name; n.Name] = name then yield n.Range ]
    | _ -> []

/// Find functions annotated with [<Test>]
let private testFunctions(parse: FSharpParseFileResults): (string list * SyntaxTree.SynBinding) list =
    let (|XunitTest|_|) str =
        match str with
        | "Fact" | "Xunit.FactAttribute"
        | "Theory" | "Xunit.TheoryAttribute" -> Some true
        | _ -> None
    let (|NUnitTest|_|) str =
        match str with
        | "Test" | "NUnit.Framework.Test" -> Some true
        | _ -> None
    let isTestAttribute(a: SyntaxTree.SynAttribute): bool =
        let ids = a.TypeName.Lid
        let string = String.concat "." [for i in ids do yield i.idText]
        match string with
        // TODO check for open NUnit.Framework before accepting plain "Test"
        | NUnitTest _ | XunitTest _ -> true
        | _ -> false
    let isTestFunction(binding: SyntaxTree.SynBinding): bool =
        let attrs = match binding with SyntaxTree.Binding(_, _, _, _, attrs, _, _, _, _, _, _, _) -> attrs
        let mutable found = false
        for list in attrs do
            for a in list.Attributes do
                if isTestAttribute(a) then
                    found <- true
        found
    let name(binding: SyntaxTree.SynBinding): string list =
        match binding with
        | SyntaxTree.Binding(_, _, _, _, _, _, _, SyntaxTree.SynPat.LongIdent(SyntaxTree.LongIdentWithDots(ids, _), _, _, _, _, _), _, _, _, _) ->
            [for i in ids do yield i.idText]
        | _ -> []
    let rec bindings(ctx: string list, m: SyntaxTree.SynModuleDecl): (string list * SyntaxTree.SynBinding) seq =
        seq {
            match m with
            | SyntaxTree.SynModuleDecl.NestedModule(outer, _, decls, _, _) ->
                let ids = match outer with SyntaxTree.ComponentInfo(_, _, _, ids, _, _, _, _) -> ids
                let ctx = ctx@[for i in ids do yield i.idText]
                for d in decls do
                    yield! bindings(ctx, d)
            | SyntaxTree.SynModuleDecl.Let(_, bindings, _) ->
                for b in bindings do
                    yield ctx@name(b), b
            | SyntaxTree.SynModuleDecl.Types(defs, _) ->
                for d in defs do
                    match d with
                    | SyntaxTree.TypeDefn(SyntaxTree.ComponentInfo(_, _, _, ids, _, _, _, _), _, members, _) ->
                        let ctx = ctx@[for i in ids do yield i.idText]
                        for m in members do
                            match m with
                            | SyntaxTree.SynMemberDefn.Member(b, _) ->
                                yield ctx@name(b), b
                            | SyntaxTree.SynMemberDefn.LetBindings(bindings, _, _, _) ->
                                for b in bindings do
                                    yield ctx@name(b), b
                            | _ -> ()
            | _ -> ()
        }
    let modules =
        match parse.ParseTree with
        | Some(SyntaxTree.ParsedInput.ImplFile(SyntaxTree.ParsedImplFileInput(_, _, _, _, _, modules, _))) -> modules
        | _ -> []
    [ for m in modules do
        let ids, decls = match m with SyntaxTree.SynModuleOrNamespace(ids, _, _, decls, _, _, _, _) -> ids, decls
        let name = [for i in ids do yield i.idText]
        for d in decls do
            for ctx, b in bindings(name, d) do
                if isTestFunction(b) then
                    yield ctx, b ]

type Server(client: ILanguageClient) =
    let docs = DocumentStore()
    let checker = FSharpChecker.Create()
    let projects = ProjectManager(checker)

    /// Get a file from docs, or read it from disk
    let getOrRead(file: FileInfo): string option =
        match docs.GetText(file) with
        | Some(text) -> Some(text)
        | None when file.Exists -> Some(File.ReadAllText(file.FullName))
        | None -> None

    /// Read a specific line from a file
    let lineContent(file: FileInfo, targetLine: int): string =
        let text = getOrRead(file) |> Option.defaultValue ""
        let reader = new StringReader(text)
        let mutable line = 0
        while line < targetLine && reader.Peek() <> -1 do
            reader.ReadLine() |> ignore
            line <- line + 1
        if reader.Peek() = -1 then
            dprintfn "Reached EOF before line %d in file %O" targetLine file.Name
            ""
        else
            reader.ReadLine()

    /// Parse a file
    let parseFile(file: FileInfo): Async<Result<FSharpParseFileResults, string>> =
        async {
            match projects.FindProjectOptions(file), getOrRead(file) with
            | Error(_), _ ->
                return Error(sprintf "Can't find symbols in %s because of error in project options" file.Name)
            | _, None ->
                return Error(sprintf "%s was closed" file.FullName)
            | Ok(projectOptions), Some(sourceText) ->
                match checker.TryGetRecentCheckResultsForFile(file.FullName, projectOptions) with
                | Some(parse, _, _) ->
                    return Ok parse
                | None ->
                    try
                        let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions(projectOptions)
                        let! parse = checker.ParseFile(file.FullName, SourceText.ofString(sourceText), parsingOptions)
                        return Ok(parse)
                    with e ->
                        return Error(e.Message)
        }

    /// Typecheck `file`
    /// If `allowCached`, will re-use cached results where the source exactly matches
    /// `allowCached` is a bad idea if upstream dependencies of `file` have been edited
    /// If `allowStale`, will re-use stale results where the source doesn't match
    /// `allowStale` only really works for simple identifier expressions like x.y
    let checkOpenFile(file: FileInfo, allowCached: bool, allowStale: bool): Async<Result<FSharpParseFileResults * FSharpCheckFileResults, Diagnostic list>> =
        async {
            match projects.FindProjectOptions(file), docs.Get(file) with
            | _, None ->
                // If file doesn't exist, there's nothing to report
                dprintfn "%s was closed" file.FullName
                return Error []
            | Error(errs), _ ->
                return Error(errs)
            | Ok(projectOptions), Some(sourceText, sourceVersion) ->
                let recompile = async {
                    let timeCheck = Stopwatch.StartNew()
                    let! force = checker.ParseAndCheckFileInProject(file.FullName, sourceVersion, SourceText.ofString(sourceText), projectOptions)
                    dprintfn "Checked %s in %dms" file.Name timeCheck.ElapsedMilliseconds
                    match force with
                    | parseResult, FSharpCheckFileAnswer.Aborted -> return Error(asDiagnostics parseResult.Errors)
                    | parseResult, FSharpCheckFileAnswer.Succeeded(checkResult) -> return Ok(parseResult, checkResult)
                }
                match checker.TryGetRecentCheckResultsForFile(file.FullName, projectOptions) with
                | Some(parseResult, checkResult, version) ->
                    if allowCached && version = sourceVersion then
                        return Ok(parseResult, checkResult)
                    else if allowCached && allowStale then
                        try
                            dprintfn "Trying to recompile %s with timeout" file.Name
                            let! worker = Async.StartChild(recompile, millisecondsTimeout=200)
                            return! worker
                        with :? TimeoutException ->
                            dprintfn "Re-compile timed out, using stale results"
                            return Ok(parseResult, checkResult)
                    else
                        return! recompile
                | _ ->
                    return! recompile
        }

    /// When did we last check each file on disk?
    let lastCheckedOnDisk = new System.Collections.Generic.Dictionary<string, DateTime>()
    // TODO there might be a thread safety issue here---is this getting called from a separate thread?
    do checker.BeforeBackgroundFileCheck.Add(fun(fileName, _) ->
        let file = FileInfo(fileName)
        lastCheckedOnDisk.[file.FullName] <- file.LastWriteTime)

    /// Figure out what files will be implicitly recompiled if we recompile `goal`
    let needsRecompile(goal: FileInfo): List<FileInfo> =
        match projects.FindProjectOptions(goal) with
        | Ok(projectOptions) ->
            // Find all projects that goal depends on, including its own project
            let projects = projects.TransitiveDeps(FileInfo(projectOptions.ProjectFileName))
            // Take all files that lead up to goal, not including itself
            let files = [for p in projects do
                            let sourceFiles = Array.map FileInfo p.SourceFiles
                            let notGoal(f: FileInfo) = f.FullName <> goal.FullName
                            yield! Array.takeWhile notGoal sourceFiles]
            // Skip files until we find a modified file, then take all remaining files
            let notModified(f: FileInfo) =
                match lastCheckedOnDisk.TryGetValue(f.FullName) with
                | true, lastChecked -> f.LastWriteTime <= lastChecked
                | _, _ -> false
            let modified = List.skipWhile notModified files
            // `goal` should always be on the list
            modified@[goal]
        | Error(_) -> []
    /// Send diagnostics to the client
    let publishErrors(file: FileInfo, errors: Diagnostic list) =
        client.PublishDiagnostics({uri=Uri("file://" + file.FullName); diagnostics=errors})
    /// Check a file
    let getErrors(file: FileInfo, check: Result<FSharpParseFileResults * FSharpCheckFileResults, Diagnostic list>): Async<Diagnostic list> =
        async {
            match check with
            | Error(errors) ->
                return errors
            | Ok(parseResult, checkResult) ->
                let parseErrors = asDiagnostics(parseResult.Errors)
                let typeErrors = asDiagnostics(checkResult.Errors)
                // This is just too slow. Also, it's sometimes wrong.
                // Find unused opens
                // let timeUnusedOpens = Stopwatch.StartNew()
                // let! unusedOpenRanges = UnusedOpens.getUnusedOpens(checkResult, fun(line) -> lineContent(file, line))
                // let unusedOpenErrors = [for r in unusedOpenRanges do yield diagnostic("Unused open", r, DiagnosticSeverity.Information)]
                // dprintfn "Found %d unused opens in %dms" unusedOpenErrors.Length timeUnusedOpens.ElapsedMilliseconds
                // Find unused declarations
                let timeUnusedDeclarations = Stopwatch.StartNew()
                let uses = checkResult.GetAllUsesOfAllSymbolsInFile()
                let unusedDeclarationRanges = UnusedDeclarations.getUnusedDeclarationRanges(Seq.toArray uses, file.Name.EndsWith(".fsx"))
                let unusedDeclarationErrors = [for r in unusedDeclarationRanges do yield diagnostic("Unused declaration", r, DiagnosticSeverity.Hint)]
                dprintfn "Found %d unused declarations in %dms" unusedDeclarationErrors.Length timeUnusedDeclarations.ElapsedMilliseconds
                // Combine
                // return parseErrors@typeErrors@unusedOpenErrors@unusedDeclarationErrors
                return parseErrors@typeErrors@unusedDeclarationErrors
        }
    let doCheck(file: FileInfo): Async<unit> =
        async {
            let! check = checkOpenFile(file, true, false)
            let! errors = getErrors(file, check)
            publishErrors(file, errors)
        }
    /// Request that `uri` be checked when the user stops doing things for 1 second
    let backgroundCheck = DebounceCheck(doCheck, 1000)
    /// Find the symbol at a position
    let symbolAt(textDocument: TextDocumentIdentifier, position: Position): Async<FSharpSymbolUse option> =
        async {
            let file = FileInfo(textDocument.uri.LocalPath)
            let! c = checkOpenFile(file, true, false)
            let line = lineContent(file, position.line)
            let maybeId = QuickParse.GetCompleteIdentifierIsland false line (position.character)
            match c, maybeId with
            | Error(errors), _ ->
                dprintfn "Check failed, ignored %d errors" (List.length errors)
                return None
            | _, None ->
                dprintfn "No identifier at %d in line '%s'" position.character line
                return None
            | Ok(_, checkResult), Some(id, endOfIdentifier, _) ->
                dprintfn "Looking at symbol %s" id
                let names = List.ofArray(id.Split('.'))
                let maybeSymbol = checkResult.GetSymbolUseAtLocation(position.line+1, endOfIdentifier, line, names)
                if maybeSymbol.IsNone then
                    dprintfn "%s in line '%s' is not a symbol use" id line
                return maybeSymbol
        }

    /// Find the exact location of a symbol within a fully-qualified name.
    /// For example, if we have `let b = Foo.bar`, and we want to find the symbol `bar` in the range `let b = [Foo.bar]`.
    let refineRenameRange(s: FSharpSymbol, file: FileInfo, range: Text.range): Range =
        let line = range.End.Line - 1
        let startColumn = if range.Start.Line - 1 < line then 0 else range.Start.Column
        let endColumn = range.End.Column
        let lineText = lineContent(file, line )
        let find = lineText.LastIndexOf(s.DisplayName, endColumn, endColumn - startColumn)
        if find = -1 then
            dprintfn "Couldn't find '%s' in line '%s'" s.DisplayName lineText
            asRange range
        else
            {
                start={line=line; character=find}
                ``end``={line=line; character=find + s.DisplayName.Length}
            }
    /// Rename one usage of a symbol
    let renameTo(newName: string, file: FileInfo, usages: FSharpSymbolUse seq): TextDocumentEdit =
        let uri = Uri("file://" + file.FullName)
        let version = docs.GetVersion(file) |> Option.defaultValue 0
        let edits = [
            for u in usages do
                let range = refineRenameRange(u.Symbol, FileInfo(u.FileName), u.RangeAlternate)
                yield {range=range; newText=newName} ]
        {textDocument={uri=uri; version=version}; edits=edits}

    let symbolPattern = Regex(@"\w+")
    /// Quickly check if a file *might* contain a symbol matching query
    let maybeMatchesQuery(query: string, file: FileInfo): string option =
        match getOrRead(file) with
        | None -> None
        | Some(text) ->
            let matches = symbolPattern.Matches(text)
            let test(m: Match) = matchesQuery(query, m.Value)
            if Seq.exists test matches then
                Some(text)
            else
                None
    let exactlyMatches(findSymbol: string, file: FileInfo): string option =
        match getOrRead(file) with
        | None -> None
        | Some(text) ->
            let matches = symbolPattern.Matches(text)
            let test(m: Match) = m.Value = findSymbol
            if Seq.exists test matches then
                Some(text)
            else
                None

    /// Find all uses of a symbol, across all open projects
    let findAllSymbolUses(symbol: FSharpSymbol): Async<List<FSharpSymbolUse>> =
        async {
            // If the symbol is private or internal, we only need to scan 1 file or project
            // TODO this only detects symbols *declared* private, many symbols are implicitly private
            let isPrivate, isInternal =
                match FSharpSymbol.GetAccessibility(symbol) with
                | Some(a) when a.IsPrivate -> true, true
                | Some(a) when a.IsInternal -> false, true
                | _ -> false, false
            // Figure out what project and file the symbol is declared in
            // This might be nothing if the symbol is declared outside the workspace
            let symbolDeclarationProject, symbolDeclarationFile =
                match symbol.DeclarationLocation with
                | None -> None, None
                | Some(range) ->
                    let f = FileInfo(range.FileName)
                    match projects.FindProjectOptions(f) with
                    | Error(_) -> None, Some(f)
                    | Ok(projectOptions) -> Some(projectOptions), Some(f)
            if isPrivate then
                dprintfn "Symbol %s is private so we will only check declaration file %A" symbol.FullName symbolDeclarationFile
            elif isInternal then
                dprintfn "Symbol %s is internal so we will onlcy check declaration project %A" symbol.FullName symbolDeclarationProject
            // Is fileName the same file symbol was declared in?
            let isSymbolFile(fileName: string) =
                match symbolDeclarationFile with None -> false | Some(f) -> f.FullName = fileName
            // Is candidate the same project that symbol was declared in?
            let isSymbolProject(candidate: FSharpProjectOptions) =
                match symbolDeclarationProject with None -> false | Some(p) -> candidate.ProjectFileName = p.ProjectFileName
            // Does fileName come after symbol in dependency order, meaning it can see symbol?
            let isVisibleFromFile(fromFile: FileInfo) =
                match symbolDeclarationFile with
                | Some(symbolFile) -> projects.IsVisible(symbolFile, fromFile)
                | _ -> true
            // Is symbol visible from file?
            let isVisibleFrom(project: FSharpProjectOptions, file: string) =
                if isPrivate then
                    isSymbolFile(file)
                elif isInternal then
                    isSymbolProject(project) && isVisibleFromFile(FileInfo(file))
                else
                    isVisibleFromFile(FileInfo(file))
            // Find all source files that can see symbol
            let visible = [
                for projectOptions in projects.OpenProjects do
                    for fileName in projectOptions.SourceFiles do
                        if isVisibleFrom(projectOptions, fileName) then
                            yield projectOptions, FileInfo(fileName)
            ]
            let visibleNames = String.concat ", " [for _, f in visible do yield f.Name]
            dprintfn "Symbol %s is visible from %s" symbol.FullName visibleNames
            // Check source files for possible symbol references using string matching
            let searchFor =
                // Attributes are referenced without the `Attribute` suffix
                // searchFor is just used to cut down the number of files we need to check,
                // so it's OK to be a little fast-and-sloppy
                if symbol.DisplayName.EndsWith("Attribute") then
                    symbol.DisplayName.Substring(0, symbol.DisplayName.Length - "Attribute".Length)
                else
                    symbol.DisplayName
            let candidates = [
                for projectOptions, sourceFile in visible do
                    match exactlyMatches(searchFor, sourceFile) with
                    | None -> ()
                    | Some(sourceText) -> yield projectOptions, sourceFile, sourceText
            ]
            let candidateNames = String.concat ", " [for _, file, _ in candidates do yield file.Name]
            dprintfn "Name %s appears in %s" searchFor candidateNames
            // Check each candidate file
            use progress = new ProgressBar(candidates.Length, sprintf "Search %d files" candidates.Length, client)
            let all = System.Collections.Generic.List<FSharpSymbolUse>()
            for projectOptions, sourceFile, sourceText in candidates do
                try
                    // Send a notification to the client updating the progress indicator
                    progress.Increment(sourceFile)

                    // Check file
                    let sourceVersion = docs.GetVersion(sourceFile) |> Option.defaultValue 0
                    let timeCheck = Stopwatch.StartNew()
                    let! _, maybeCheck = checker.ParseAndCheckFileInProject(sourceFile.FullName, sourceVersion, SourceText.ofString(sourceText), projectOptions)
                    dprintfn "Checked %s in %dms" sourceFile.Name timeCheck.ElapsedMilliseconds
                    match maybeCheck with
                    | FSharpCheckFileAnswer.Aborted -> dprintfn "Aborted checking %s" sourceFile.Name
                    | FSharpCheckFileAnswer.Succeeded(check) ->
                        let uses = check.GetUsesOfSymbolInFile(symbol)
                        for u in uses do
                            all.Add(u)
                with e ->
                    dprintfn "Error checking %s: %s" sourceFile.Name e.Message
            return List.ofSeq(all)
        }

    /// Tell the user if we run out of memory
    /// TODO add a setting to increase max memory
    let maxMemoryWarning() =
        let message = sprintf "Reached max memory %d MB" checker.MaxMemory
        client.ShowMessage({``type``=MessageType.Warning; message=message})
    let _ = checker.MaxMemoryReached.Add(maxMemoryWarning)

    /// Remember the last completion list for ResolveCompletionItem
    let mutable lastCompletion: FSharpDeclarationListInfo option = None

    /// Defer initialization operations until Initialized() is called,
    /// so that the client-side code int client/extension.ts starts running immediately
    let mutable deferredInitialize = async { () }

    interface ILanguageServer with
        member this.Initialize(p: InitializeParams) =
            async {
                match p.rootUri with
                | Some root ->
                    dprintfn "Add workspace root %s" root.LocalPath
                    deferredInitialize <- projects.AddWorkspaceRoot(DirectoryInfo(root.LocalPath))
                | _ -> dprintfn "No root URI in initialization message %A" p
                return {
                    capabilities =
                        { defaultServerCapabilities with
                            hoverProvider = true
                            completionProvider = Some({resolveProvider=true; triggerCharacters=['.']})
                            signatureHelpProvider = Some({triggerCharacters=['('; ',']})
                            documentSymbolProvider = true
                            codeLensProvider = Some({resolveProvider=true})
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
            deferredInitialize
        member this.Shutdown(): Async<unit> =
            async { () }
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams): Async<unit> =
            async {
                dprintfn "New configuration %s" (p.ToString())
            }
        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams): Async<unit> =
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                // Store text in docs
                docs.Open(p)
                // Create a progress bar if #todo > 1
                let todo = needsRecompile(file)
                use progress = new ProgressBar(todo.Length, sprintf "Check %d files" todo.Length, client, todo.Length <= 1)
                use increment = checker.BeforeBackgroundFileCheck.Subscribe(fun (fileName, _) -> progress.Increment(FileInfo(fileName)))
                do! doCheck(file)
            }
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams): Async<unit> =
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                docs.Change(p)
                backgroundCheck.CheckLater(file)
            }
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams): Async<unit> = TODO()
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): Async<TextEdit list> = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams): Async<unit> =
            async {
                let targetFile = FileInfo(p.textDocument.uri.LocalPath)
                let todo = [ for fromFile in docs.OpenFiles() do
                                if projects.IsVisible(targetFile, fromFile) then
                                    yield fromFile ]
                use progress = new ProgressBar(todo.Length, sprintf "Check %d files" todo.Length, client, todo.Length <= 1)
                for file in todo do
                    progress.Increment(file)
                    let! check = checkOpenFile(file, false, false)
                    let! errors = getErrors(file, check)
                    publishErrors(file, errors)
            }
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams): Async<unit> =
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                docs.Close(p)
                // Only show errors for open files
                publishErrors(file, [])
            }
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): Async<unit> =
            async {
                for change in p.changes do
                    let file = FileInfo(change.uri.LocalPath)
                    dprintfn "Watched file %s %O" file.FullName change.``type``
                    if file.Name.EndsWith(".fsproj") || file.Name.EndsWith(".fsx") then
                        match change.``type`` with
                        | FileChangeType.Created ->
                            projects.NewProjectFile(file)
                        | FileChangeType.Changed ->
                            projects.UpdateProjectFile(file)
                        | FileChangeType.Deleted ->
                            projects.DeleteProjectFile(file)
                    elif file.Name.EndsWith(".sln") then
                        match change.``type`` with
                        | FileChangeType.Created ->
                            projects.UpdateSlnFile(file)
                        | FileChangeType.Changed ->
                            projects.UpdateSlnFile(file)
                        | FileChangeType.Deleted ->
                            projects.DeleteSlnFile(file)
                    elif file.Name = "project.assets.json" then
                        projects.UpdateAssetsJson(file)
                // Re-check all open files
                // In theory we could optimize this by only re-checking descendents of changed projects,
                // but in practice that will make little difference
                for f in docs.OpenFiles() do
                    backgroundCheck.CheckLater(f)
            }
        member this.Completion(p: TextDocumentPositionParams): Async<CompletionList option> =
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                dprintfn "Autocompleting at %s(%d,%d)" file.FullName p.position.line p.position.character
                let line = lineContent(file, p.position.line)
                let partialName = QuickParse.GetPartialLongNameEx(line, p.position.character-1)
                // When a partial identifier is not present, stale completions are very inaccurate
                // For example Some(1).? will complete top-level names rather than the members of Option
                // Therefore, we will always re-check the file, even if it takes a while
                let noPartialName = partialName.QualifyingIdents.IsEmpty && partialName.PartialIdent = ""
                // TODO when this is the only edited line, and the line looks like x.y.z.?, then stale completions are quite accurate
                let! c = checkOpenFile(file, true, not(noPartialName))
                dprintfn "Finished typecheck, looking for completions..."
                match c with
                | Error errors ->
                    dprintfn "Check failed, ignored %d errors" (List.length(errors))
                    return None
                | Ok(parseResult, checkResult) ->
                        let declarations = checkResult.GetDeclarationListInfo(Some parseResult, p.position.line+1, line, partialName)
                        lastCompletion <- Some declarations
                        dprintfn "Found %d completions" declarations.Items.Length
                        return Some(asCompletionList(declarations))
            }
        member this.Hover(p: TextDocumentPositionParams): Async<Hover option> =
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                let! c = checkOpenFile(file, true, false)
                let line = lineContent(file, p.position.line)
                let maybeId = QuickParse.GetCompleteIdentifierIsland false line (p.position.character)
                match c, maybeId with
                | Error(errors), _ ->
                    dprintfn "Check failed, ignored %d errors" (List.length(errors))
                    return None
                | _, None ->
                    dprintfn "No identifier at %s(%d, %d)" file.FullName p.position.line p.position.character
                    return None
                | Ok(parseResult, checkResult), Some(id, _, _) ->
                    dprintfn "Hover over %s" id
                    let ids = List.ofArray(id.Split('.'))
                    let tips = checkResult.GetToolTipText(p.position.line+1, p.position.character+1, line, ids, FSharpTokenTag.Identifier)
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
                            let! resolved = TipFormatter.resolveDocs(p, candidate)
                            result <- resolved
                return result
            }
        member this.SignatureHelp(p: TextDocumentPositionParams): Async<SignatureHelp option> =
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                let! c = checkOpenFile(file, true, true)
                match c with
                | Error errors ->
                    dprintfn "Check failed, ignored %d errors" (List.length(errors))
                    return None
                | Ok(parseResult, checkResult) ->
                    let line = lineContent(file, p.position.line)
                    match findMethodCallBeforeCursor(line, p.position.character) with
                    | None ->
                        dprintfn "No method call in line %s" line
                        return None
                    | Some endOfMethodName ->
                        match QuickParse.GetCompleteIdentifierIsland false line (endOfMethodName - 1) with
                        | None ->
                            dprintfn "No identifier before column %d in %s" (endOfMethodName - 1) line
                            return None
                        | Some(id, _, _) ->
                            dprintfn "Looking for overloads of %s" id
                            let names = List.ofArray(id.Split('.'))
                            let overloads = checkResult.GetMethods(p.position.line+1, endOfMethodName, line, Some names)
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
                let file = FileInfo(p.textDocument.uri.LocalPath)
                let! maybeParse = parseFile(file)
                match maybeParse with
                | Error e ->
                    dprintfn "%s" e
                    return []
                | Ok parse ->
                    let flat = findDeclarations(parse)
                    return List.map asSymbolInformation flat
            }
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams): Async<SymbolInformation list> =
            async {
                dprintfn "Looking for symbols matching `%s`" p.query
                // Read open projects until we find at least 50 symbols that match query
                let all = System.Collections.Generic.List<SymbolInformation>()
                // TODO instead of checking open projects, check all .fs files, using default parsing options
                for projectOptions in projects.OpenProjects do
                    dprintfn "...check project %s" projectOptions.ProjectFileName
                    for sourceFileName in projectOptions.SourceFiles do
                        let sourceFile = FileInfo(sourceFileName)
                        if all.Count < 50 then
                            dprintfn "...scan %s" sourceFile.Name
                            match maybeMatchesQuery(p.query, sourceFile) with
                            | None -> ()
                            | Some(sourceText) ->
                                try
                                    dprintfn "...parse %s" sourceFile.Name
                                    let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions(projectOptions)
                                    let! parse = checker.ParseFile(sourceFile.FullName, SourceText.ofString(sourceText), parsingOptions)
                                    for declaration, container in findDeclarations(parse) do
                                        if matchesQuery(p.query, declaration.Name) then
                                            all.Add(asSymbolInformation(declaration, container))
                                with e ->
                                    dprintfn "Error parsing %s: %s" sourceFile.Name e.Message
                return List.ofSeq(all)
            }
        member this.CodeActions(p: CodeActionParams): Async<Command list> = TODO()
        member this.CodeLens(p: CodeLensParams): Async<List<CodeLens>> =
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                match projects.FindProjectOptions(file), getOrRead(file) with
                | Ok(projectOptions), Some(sourceText) ->
                    let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions(projectOptions)
                    let! parse = checker.ParseFile(file.FullName, SourceText.ofString(sourceText), parsingOptions)
                    if file.Name.EndsWith(".fs") then
                        let fns = testFunctions(parse)
                        let fsproj = FileInfo(projectOptions.ProjectFileName)
                        return [ for id, bindings in fns do
                                    yield asRunTest(fsproj, id, bindings)
                                    yield asDebugTest(fsproj, id, bindings) ]
                    else if file.Name.EndsWith(".fsi") then
                        return
                            [ for name, range in findSignatureDeclarations(parse) do
                                yield asGoToImplementation(name, file, range) ]
                    else
                        dprintfn "Don't know how to compute code lenses on extension %s" file.Extension
                        return []
                | Error(e), _ ->
                    dprintfn "Failed to create code lens because project options failed to load: %A" e
                    return []
                | _, None ->
                    dprintfn "Failed to create code lens because file %s does not exist" file.FullName
                    return []
            }
        member this.ResolveCodeLens(p: CodeLens): Async<CodeLens> =
            async {
                if p.data <> JsonValue.Null then
                    dprintfn "Resolving %A" p.data
                    let fsi, name = goToImplementationData(p)
                    if not(fsi.Extension = ".fsi") then
                        raise(Exception(sprintf "Signature file %s should end with .fsi" fsi.Name))
                    let file = FileInfo(fsi.FullName.Substring(0, fsi.FullName.Length - 1))
                    match projects.FindProjectOptions(file), getOrRead(file) with
                    | Ok(projectOptions), Some(sourceText) ->
                        let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions(projectOptions)
                        let! parse = checker.ParseFile(file.FullName, SourceText.ofString(sourceText), parsingOptions)
                        match findSignatureImplementation(parse, name) with
                        | [range] ->
                            return resolveGoToImplementation(p, file, range)
                        | [] ->
                            dprintfn "Signature %A has no implementation in %s" name file.Name
                            return resolveMissingGoToImplementation(p, fsi)
                        | many ->
                            dprintfn "Signature %A has multiple implementations in %s: %A" name file.Name many
                            // Go to the first overload
                            // This is wrong but still useful
                            let range = many.Head
                            return resolveGoToImplementation(p, file, range)
                    | Error(e), _ ->
                        dprintfn "Failed to resolve code lens because project options failed to load: %A" e
                        return p
                    | _, None ->
                        dprintfn "Failed to resolve code lens because file %s does not exist" file.FullName
                        return p
                else return p
            }
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
                    let renames = [for fileName, uses in byFile do yield renameTo(p.newName, FileInfo(fileName), uses)]
                    return {documentChanges=List.ofSeq(renames)}
            }
        member this.ExecuteCommand(p: ExecuteCommandParams): Async<unit> = TODO()
        member this.DidChangeWorkspaceFolders(p: DidChangeWorkspaceFoldersParams): Async<unit> =
            async {
                for root in p.event.added do
                    let file = FileInfo(root.uri.LocalPath)
                    do! projects.AddWorkspaceRoot(file.Directory)
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
