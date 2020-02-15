module FSharpLanguageServer.Program

open LSP.Log
open FSharp.Compiler
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
open Config
open FSharp.Compiler.Text
open Fantomas


module Ast = FSharp.Compiler.Ast

let private TODO() = raise (Exception "TODO")
type private dict<'a, 'b> = System.Collections.Concurrent.ConcurrentDictionary<'a, 'b>

/// Look for a method call like foo.MyMethod() before the cursor
/// (exposed for testing)
let findMethodCallBeforeCursor(lineContent: string, cursor: int): int option = 
    let mutable found = -1
    let mutable parenDepth = 0
    for i in (min (cursor-1) lineContent.Length-1) .. -1 .. 0 do 
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

/// Find top-level open directives that appear at the top of the top-level modules
let private findOpenDirectives(parse: FSharpParseFileResults) =
    match parse.ParseTree with
    | Some(Ast.ParsedInput.SigFile(Ast.ParsedSigFileInput(_, _, _, _, modules))) ->
        modules 
        |> List.collect (fun (Ast.SynModuleOrNamespaceSig(_, _, _, decls, _, _, _, _)) -> decls)
        |> List.choose(
            function
            | Ast.SynModuleSigDecl.Open(longId, range) -> Some(longId, range)
            | _ -> None)
    | Some(Ast.ParsedInput.ImplFile(Ast.ParsedImplFileInput(_, _, _, _, _, modules, _))) ->
        modules 
        |> List.collect (fun (Ast.SynModuleOrNamespace(_, _, _, decls, _, _, _, _)) -> decls)
        |> List.choose(
            function
            | Ast.SynModuleDecl.Open(longId, range) -> Some(longId.Lid (*??*), range)
            | _ -> None)
    | _ -> []

/// Find searchable declarations
let private findDeclarations(parse: FSharpParseFileResults) = 
    let items = 
        match parse.ParseTree with 
        | Some(Ast.ParsedInput.SigFile(Ast.ParsedSigFileInput(_, _, _, _, modules))) -> 
            Navigation.getNavigationFromSigFile(modules).Declarations
        | Some(Ast.ParsedInput.ImplFile(Ast.ParsedImplFileInput(_, _, _, _, _, modules, _))) -> 
            Navigation.getNavigationFromImplFile(modules).Declarations
        | _ -> [||]
    [ for i in items do 
        yield i.Declaration, None
        for n in i.Nested do 
            yield n, Some(i.Declaration) ]

let private findSignatureDeclarations(parse: FSharpParseFileResults) = 
    match parse.ParseTree with 
    | Some(Ast.ParsedInput.SigFile(Ast.ParsedSigFileInput(_, _, _, _, modules))) -> 
        let items = Navigation.getNavigationFromSigFile(modules)
        [ for i in items.Declarations do 
            for n in i.Nested do 
                yield [i.Declaration.Name; n.Name], n.Range ]
    | _ -> []

let private findSignatureImplementation(parse: FSharpParseFileResults, name: string list) = 
    match parse.ParseTree with 
    | Some(Ast.ParsedInput.ImplFile(Ast.ParsedImplFileInput(_, _, _, _, _, modules, _))) -> 
        let items = Navigation.getNavigationFromImplFile(modules)
        [ for i in items.Declarations do 
            for n in i.Nested do 
                if [i.Declaration.Name; n.Name] = name then yield n.Range ]
    | _ -> []

/// Find functions annotated with [<Test>]
let private testFunctions(parse: FSharpParseFileResults): (string list * Ast.SynBinding) list = 
    let (|XunitTest|_|) str =
        match str with
        | "Fact" | "Xunit.FactAttribute"
        | "Theory" | "Xunit.TheoryAttribute" -> Some true
        | _ -> None
    let (|NUnitTest|_|) str =
        match str with
        | "Test" | "NUnit.Framework.Test" -> Some true
        | _ -> None
    let isTestAttribute(a: Ast.SynAttribute): bool = 
        let ids = a.TypeName.Lid
        let string = String.concat "." [for i in ids do yield i.idText]
        match string with 
        // TODO check for open NUnit.Framework before accepting plain "Test"
        | NUnitTest _ | XunitTest _ -> true
        | _ -> false
    let isTestFunction(binding: Ast.SynBinding): bool = 
        let attrs = match binding with Ast.Binding(_, _, _, _, attrs, _, _, _, _, _, _, _) -> attrs
        attrs 
        |> Seq.map (fun l -> l.Attributes)
        |> Seq.exists (List.exists isTestAttribute)
    let name(binding: Ast.SynBinding): string list = 
        match binding with 
        | Ast.Binding(_, _, _, _, _, _, _, Ast.SynPat.LongIdent(Ast.LongIdentWithDots(ids, _), _, _, _, _, _), _, _, _, _) -> 
            [for i in ids do yield i.idText]
        | _ -> []
    let rec bindings(ctx: string list, m: Ast.SynModuleDecl): (string list * Ast.SynBinding) seq = 
        seq {
            match m with 
            | Ast.SynModuleDecl.NestedModule(outer, _, decls, _, _) -> 
                let ids = match outer with Ast.ComponentInfo(_, _, _, ids, _, _, _, _) -> ids   
                let ctx = ctx@[for i in ids do yield i.idText]
                for d in decls do 
                    yield! bindings(ctx, d)
            | Ast.SynModuleDecl.Let(_, bindings, _) -> 
                for b in bindings do 
                    yield ctx@name(b), b
            | Ast.SynModuleDecl.Types(defs, _) -> 
                for d in defs do 
                    match d with 
                    | Ast.TypeDefn(Ast.ComponentInfo(_, _, _, ids, _, _, _, _), _, members, _) -> 
                        let ctx = ctx@[for i in ids do yield i.idText]
                        for m in members do 
                            match m with 
                            | Ast.SynMemberDefn.Member(b, _) -> 
                                yield ctx@name(b), b
                            | Ast.SynMemberDefn.LetBindings(bindings, _, _, _) -> 
                                for b in bindings do 
                                    yield ctx@name(b), b
                            | _ -> ()
            | _ -> ()
        }
    let modules = 
        match parse.ParseTree with 
        | Some(Ast.ParsedInput.ImplFile(Ast.ParsedImplFileInput(_, _, _, _, _, modules, _))) -> modules
        | _ -> []
    [ for m in modules do 
        let ids, decls = match m with Ast.SynModuleOrNamespace(ids, _, _, decls, _, _, _, _) -> ids, decls
        let name = [for i in ids do yield i.idText]
        for d in decls do 
            for ctx, b in bindings(name, d) do 
                if isTestFunction(b) then 
                    yield ctx, b ]

type Server(client: ILanguageClient) as this = 
    let docs = DocumentStore()
    let checker = FSharpChecker.Create()
    let projects = ProjectManager(checker)
    let mutable codelensShowReferences = true
    let mutable showUnusedDeclarations = true

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

    let getParsingOptions(projectOptions) =
        let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions(projectOptions)
        { parsingOptions with ConditionalCompilationDefines = projects.ConditionalCompilationDefines}

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
                        let parsingOptions = getParsingOptions(projectOptions)
                        let sourceText = SourceText.ofString sourceText
                        let! parse = checker.ParseFile(file.FullName, sourceText, parsingOptions)
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
                    let sourceText = SourceText.ofString sourceText
                    let! force = checker.ParseAndCheckFileInProject(file.FullName, sourceVersion, sourceText, projectOptions)
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
    let lastCheckedOnDisk = dict<string, DateTime>()
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

    /// Maps the file paths to the hash code of used symbols
    let fileSymbolUses = dict<string, string[]>()

    /// Check a file
    let getErrors(file: FileInfo, check: Result<FSharpParseFileResults * FSharpCheckFileResults, Diagnostic list>): Async<Diagnostic list> = 
        async {
            match check with
            | Error(errors) ->
                return errors
            | Ok(parseResult, checkResult) -> 
                let parseErrors = asDiagnostics(parseResult.Errors)
                let typeErrors = asDiagnostics(checkResult.Errors)
                let! uses = checkResult.GetAllUsesOfAllSymbolsInFile()
                fileSymbolUses.[file.FullName] <- Array.map (fun (x: FSharpSymbolUse) -> x.Symbol.FullName) uses
                // This is just too slow. Also, it's sometimes wrong.
                // Find unused opens
                // let timeUnusedOpens = Stopwatch.StartNew()
                // let! unusedOpenRanges = UnusedOpens.getUnusedOpens(checkResult, fun(line) -> lineContent(file, line))
                // let unusedOpenErrors = [for r in unusedOpenRanges do yield diagnostic("Unused open", r, DiagnosticSeverity.Information)]
                // dprintfn "Found %d unused opens in %dms" unusedOpenErrors.Length timeUnusedOpens.ElapsedMilliseconds
                let unusedOpenErrors = []

                // Find unused declarations
                let unusedDeclarationErrors = 
                    if showUnusedDeclarations then
                        let timeUnusedDeclarations = Stopwatch.StartNew()
                        let unusedDeclarationRanges = UnusedDeclarations.getUnusedDeclarationRanges(uses, file.Name.EndsWith(".fsx"))
                        let unusedDeclarationErrors = [for r in unusedDeclarationRanges do yield diagnostic("Unused declaration", r, DiagnosticSeverity.Hint)]
                        dprintfn "Found %d unused declarations in %dms" unusedDeclarationErrors.Length timeUnusedDeclarations.ElapsedMilliseconds
                        unusedDeclarationErrors
                    else []

                // Combine
                return parseErrors@typeErrors@unusedOpenErrors@unusedDeclarationErrors
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
                dprintfn "symbol at: Check failed, ignored %d errors" (List.length errors)
                return None
            | _, None -> 
                dprintfn "No identifier at %d in line '%s'" position.character line 
                return None
            | Ok(_, checkResult), Some(id, endOfIdentifier, _) -> 
                dprintfn "Looking at symbol %s" id
                let names = List.ofArray(id.Split('.'))
                let! maybeSymbol = checkResult.GetSymbolUseAtLocation(position.line+1, endOfIdentifier, line, names)
                if maybeSymbol.IsNone then
                    dprintfn "%s in line '%s' is not a symbol use" id line
                return maybeSymbol
        }

    /// Find the exact location of a symbol within a fully-qualified name.
    /// For example, if we have `let b = Foo.bar`, and we want to find the symbol `bar` in the range `let b = [Foo.bar]`.
    let refineRange(s: string, file: FileInfo, range: Range.range): Range = 
        let line = if range.EndColumn = 0 then range.End.Line - 2 else range.End.Line - 1
        let lineText = lineContent(file, line)
        let startColumn = if range.Start.Line - 1 < line then 0 else range.Start.Column
        let endColumn = if range.EndColumn = 0 then lineText.Length else range.End.Column
        let find = lineText.LastIndexOf(s, endColumn, endColumn - startColumn)
        if find = -1 then
            dprintfn "Couldn't find '%s' in line '%s'" s lineText 
            asRange range
        else 
            {
                start={line=line; character=find}
                ``end``={line=line; character=find + s.Length}
            }
    /// Rename one usage of a symbol
    let renameTo(newName: string, file: FileInfo, usages: FSharpSymbolUse seq): TextDocumentEdit = 
        let uri = Uri("file://" + file.FullName)
        let version = docs.GetVersion(file) |> Option.defaultValue 0
        let edits = [
            for u in usages do 
                let range = refineRange(u.Symbol.DisplayName, FileInfo(u.FileName), u.RangeAlternate)
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

    let findReferenceK(symbol: FSharpSymbol, K: FSharpSymbolUse -> unit): Async<unit> =
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
                dprintfn "Symbol %s is internal so we will only check declaration project %A" symbol.FullName symbolDeclarationProject
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
            for projectOptions, sourceFile, sourceText in candidates do 
                try
                    // Send a notification to the client updating the progress indicator
                    progress.Increment(sourceFile)
                    
                    // Check file
                    let sourceVersion = docs.GetVersion(sourceFile) |> Option.defaultValue 0
                    let timeCheck = Stopwatch.StartNew()
                    let sourceText = SourceText.ofString sourceText
                    let! _, maybeCheck = checker.ParseAndCheckFileInProject(sourceFile.FullName, sourceVersion, sourceText, projectOptions)
                    dprintfn "Checked %s in %dms" sourceFile.Name timeCheck.ElapsedMilliseconds
                    match maybeCheck with 
                    | FSharpCheckFileAnswer.Aborted -> dprintfn "Aborted checking %s" sourceFile.Name
                    | FSharpCheckFileAnswer.Succeeded(check) -> 
                        let! uses = check.GetUsesOfSymbolInFile(symbol)
                        for u in uses do 
                            K u
                with e -> 
                    dprintfn "Error checking %s: %s" sourceFile.Name e.Message
        }


    /// Find the number of references to a symbol
    let findReferenceCount(node: NavigationDeclarationItem) =
        async {
            let range = node.Range
            let! sym  = symbolAt({uri = Uri(range.FileName)}, {line = range.StartLine - 1; character = range.StartColumn})
            match sym with
            | Some sym ->
                let sym = sym.Symbol.FullName
                let mutable cnt = 0
                for KeyValue(_, v) in fileSymbolUses do
                    for v in v do
                        if v = sym then cnt <- cnt + 1
                return cnt
            | _ ->
                dprintfn "findReferenceCount: symbol resolve fails. node = %A"
                    (node.Range.FileName, node.Range.StartLine, node.Range.StartColumn,
                     node.bodyRange.StartLine, node.bodyRange.StartColumn,
                     node.Glyph, node.Name, node.Kind, node.Access, node.FSharpEnclosingEntityKind
                    )
                return 0
        }

    /// Find all uses of a symbol, across all open projects
    let findAllSymbolUses(symbol: FSharpSymbol): Async<List<FSharpSymbolUse>> = 
        async {
            let all = ResizeArray<FSharpSymbolUse>()
            do! findReferenceK(symbol, all.Add)
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


    let documentFormatting(filepath: string, filerange: Range option, opts: DocumentFormattingOptions, opts_ex: Map<string, string>): Async<TextEdit list> =
      async {
          let file = FileInfo(filepath)

          match projects.FindProjectOptions(file), getOrRead(file) with
          | Error _, _
          | _, None _ -> return []
          | Ok(proj), Some content ->

          let proj = getParsingOptions proj
          let is_fsi  = file.Extension = ".fsi"

          // TODO config
          let fmtOpts =
            { FormatConfig.FormatConfig.Default with
                IndentSpaceNum = opts.tabSize
                PageWidth = 120
                SemicolonAtEndOfLine = false
                SpaceBeforeArgument = false
                SpaceBeforeColon = false
                SpaceAfterComma = true
                SpaceAfterSemicolon = true
                IndentOnTryWith = false }


          let nlines = Seq.fold (fun n c -> if c = '\n' then n+1 else n) 0 content

          try
              match filerange with
              | None       -> 
                dprintfn "Formatting document %A with options: %A" file.FullName fmtOpts
                let! formatted = CodeFormatter.FormatDocumentAsync(file.FullName, SourceOrigin.SourceString content, fmtOpts, proj, checker)
                return [ { range = {start={line=0; character=0}; ``end``={line=nlines+1; character=0}}; newText = formatted } ]
              | Some range -> 
                dprintfn "Formatting document %A, selection %A with options: %A" file.FullName range fmtOpts
                (*let fantomas_range = *)
                  (*{start={line=range.start.line+1*)
                          (*character=range.start.character}*)
                   (*``end``={line=range.``end``.line+1*)
                            (*character=range.``end``.character}}*)
                let! formatted = CodeFormatter.FormatSelectionAsync(file.FullName, (asFsRange file.FullName range), SourceOrigin.SourceString content, fmtOpts, proj, checker)
                return [ { range = range; newText = formatted } ]
          with ex -> 
              dprintfn "DocumentFormatting: %O" ex
              return []
      }


    interface ILanguageServer with 
        member __.Initialize(p: InitializeParams) =
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
                            codeActionProvider = true
                            codeLensProvider = Some({resolveProvider=true})
                            workspaceSymbolProvider = true
                            definitionProvider = true
                            referencesProvider = true
                            renameProvider = true
                            documentFormattingProvider = true
                            documentRangeFormattingProvider = true
                            textDocumentSync = 
                                { defaultTextDocumentSyncOptions with 
                                    openClose = true 
                                    save = Some({ includeText = false })
                                    change = TextDocumentSyncKind.Incremental 
                                } 
                        }
                }
            }
        member __.Initialized(): Async<unit> = 
            deferredInitialize
        member __.Shutdown(): Async<unit> = 
            async { () }
        member __.DidChangeConfiguration(p: DidChangeConfigurationParams): Async<unit> =
            async {
                let fsconfig = FSharpLanguageServerConfig.Parse(p.settings.ToString()).Fsharp
                projects.ConditionalCompilationDefines <- List.ofArray fsconfig.Project.Define
                projects.OtherCompilerFlags <- List.ofArray fsconfig.Project.OtherFlags
                codelensShowReferences <- fsconfig.Codelens.References
                showUnusedDeclarations <- fsconfig.Analysis.UnusedDeclaration
                ProjectCracker.includeCompileBeforeItems <- fsconfig.Project.IncludeCompileBefore
                dprintfn "New configuration %O" (fsconfig.JsonValue)

            }
        member __.DidOpenTextDocument(p: DidOpenTextDocumentParams): Async<unit> = 
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
        member __.DidChangeTextDocument(p: DidChangeTextDocumentParams): Async<unit> = 
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                docs.Change(p)
                backgroundCheck.CheckLater(file)
            }
        member __.WillSaveTextDocument(p: WillSaveTextDocumentParams): Async<unit> = TODO()
        member __.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): Async<TextEdit list> = TODO()
        member __.DidSaveTextDocument(p: DidSaveTextDocumentParams): Async<unit> = 
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
        member __.DidCloseTextDocument(p: DidCloseTextDocumentParams): Async<unit> = 
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                docs.Close(p)
                // Only show errors for open files
                publishErrors(file, [])
            }
        member __.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): Async<unit> = 
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
        member __.Completion(p: TextDocumentPositionParams): Async<CompletionList option> =
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
                        let! declarations = checkResult.GetDeclarationListInfo(Some parseResult, p.position.line+1, line, partialName)
                        lastCompletion <- Some declarations 
                        dprintfn "Found %d completions" declarations.Items.Length
                        return Some(asCompletionList(declarations))
            }
        member __.Hover(p: TextDocumentPositionParams): Async<Hover option> = 
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
                    let! tips = checkResult.GetToolTipText(p.position.line+1, p.position.character+1, line, ids, FSharpTokenTag.Identifier)
                    return Some(asHover(tips))
            }
        // Add documentation to a completion item
        // Generating documentation is an expensive step, so we want to defer it until the user is actually looking at it
        member __.ResolveCompletionItem(p: CompletionItem): Async<CompletionItem> = 
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
        member __.SignatureHelp(p: TextDocumentPositionParams): Async<SignatureHelp option> = 
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
                            let! overloads = checkResult.GetMethods(p.position.line+1, endOfMethodName, line, Some names)
                            let signature(i: FSharpMethodGroupItem) = asSignatureInformation(overloads.MethodName, i)
                            let sigs = Array.map signature overloads.Methods |> List.ofArray
                            let activeParameter = countCommas(line, endOfMethodName, p.position.character)
                            let activeDeclaration = findCompatibleOverload(activeParameter, overloads.Methods)
                            dprintfn "Found %d overloads" overloads.Methods.Length
                            return Some({signatures=sigs; activeSignature=activeDeclaration; activeParameter=Some activeParameter})
            }
        member __.GotoDefinition(p: TextDocumentPositionParams): Async<Location list> = 
            async {
                let! maybeSymbol = symbolAt(p.textDocument, p.position)
                match maybeSymbol with 
                | None -> return []
                | Some s -> return declarationLocation s.Symbol |> Option.toList
            }
        member __.FindReferences(p: ReferenceParams): Async<Location list> = 
            async {
                let! maybeSymbol = symbolAt(p.textDocument, p.position)
                match maybeSymbol with 
                | None -> return [] 
                | Some s -> 
                    let! uses = findAllSymbolUses(s.Symbol)
                    return List.map useLocation uses
            }
        member __.DocumentHighlight(p: TextDocumentPositionParams): Async<DocumentHighlight list> = TODO()
        member __.DocumentSymbols(p: DocumentSymbolParams): Async<SymbolInformation list> =
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
        member __.WorkspaceSymbols(p: WorkspaceSymbolParams): Async<SymbolInformation list> = 
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
                            | Some sourceText ->
                                try
                                    dprintfn "...parse %s" sourceFile.Name
                                    let parsingOptions = getParsingOptions(projectOptions)
                                    let sourceText = SourceText.ofString sourceText
                                    let! parse = checker.ParseFile(sourceFile.FullName, sourceText, parsingOptions)
                                    for declaration, container in findDeclarations(parse) do 
                                        if matchesQuery(p.query, declaration.Name) then 
                                            all.Add(asSymbolInformation(declaration, container))
                                with e -> 
                                    dprintfn "Error parsing %s: %s" sourceFile.Name e.Message
                return List.ofSeq(all)
            }

        /// <summary>
        ///      match: [fsharp 39: typecheck] [E] The value, namespace, type or module 'Thread' is not defined.
        ///      then:  1. search a reflection-based cache for a matching entry. if found, suggest opening a module/namespace
        ///             2. search for workspace symbol. if found, suggest opening a module/namespace
        ///       TODO  3. search for nuget packages 
        ///       TODO  4. off-by-one corrections 
        /// TODO match: [fsharp] [H] Unused declaration
        ///      then:  1. offer refactoring to _
        ///             2. offer refactoring to __
        /// TODO match: [fsharp 39: typecheck] [E] The value or constructor 'fancy' is not defined.
        ///      then:  1. offer create binding
        ///             2. offer create class
        /// TODO match: [fsharp 39: typecheck] [E] The field, constructor or member 'Gah' is not defined.
        ///      then:  1. offer create field
        ///             2. offer create member
        /// TODO match: [fsharp 366: typecheck] [E] No implementation was given for 'IDisposable.Dispose() : unit'. 
        ///             Note that all interface members must be implemented and listed under an appropriate 'interface' declaration, e.g. 'interface ... with member ...'.
        ///      then:  1. offer implement interface
        /// TODO match: [fsharp 855: typecheck] [E] No abstract or interface member was found that corresponds to this override
        ///      then:  1. offer adding it to the interface
        /// </summary>
        member __.CodeActions(p: CodeActionParams): Async<CodeAction list> = 
            let searchDiags (ds: Diagnostic seq) (r_message: string) =
                let r_message = Regex(r_message)
                ds |> Seq.choose(fun (d: Diagnostic) -> 
                    let _match = r_message.Match(d.message)
                    if _match.Success then Some _match
                    else None) 
                |> List.ofSeq
            let s_diags = searchDiags p.context.diagnostics
            let c_diags r_message = s_diags r_message |> List.map (fun m -> m.Groups.[1].Captures.[0].Value) |> List.tryHead
            let e_diags r_message = s_diags r_message |> List.isEmpty |> not

            let no_name                  = c_diags @"The value, namespace, type or module '(.*)' is not defined"
            let unused_declarations      = e_diags @"Unused declaration"
            let no_binding               = c_diags @"The value or constructor '(.*)' is not defined"
            let no_member                = c_diags @"The field, constructor or member '(.*)' is not defined"
            let interface_notimplemented = c_diags @"No implementation was given for '(.*)'"
            let interface_nomember       = e_diags @"No abstract or interface member was found that corresponds to this override"
            let file = FileInfo(p.textDocument.uri.LocalPath)
            let proj = projects.FindProjectOptions(file)
            let version = docs.GetVersion(file) |> Option.defaultValue 0
            let vdoc = {uri=p.textDocument.uri; version=version} 

            let openQuickFixAction openInsertRange openTarget =
                let _open = sprintf "open %s" openTarget
                {   defaultQuickFixAction 
                    with title = _open
                         edit = Some { documentChanges = [ { 
                            textDocument = vdoc
                            edits = [{ range = openInsertRange; newText = _open + "\n" }]  } ]} }

            let useFullyQualifiedQuickFix fullname range =
                {   defaultQuickFixAction 
                    with title = sprintf "Fully-qualified form: '%s'" fullname
                         edit = Some { documentChanges = [ { 
                                    textDocument = vdoc
                                    edits = [{ range = range; newText = fullname }]  } ]} }

            let openOrUseFullyQualifiedQuickFix (range: Range) (symquery: seq<_>) (check: Result<(FSharpParseFileResults*FSharpCheckFileResults),_>) (asmquery: FSharpEntity list) (actions: ResizeArray<_>) = 
                let fsRange = asFsRange file.FullName range
                let openInsertionPoint = 
                    match check with
                    | Ok(rparse, _) -> 
                        let openDirectives = 
                            findOpenDirectives rparse
                            |> List.filter (fun (_, od_range) -> od_range.EndLine < fsRange.StartLine)
                        match openDirectives.Length with
                        | 0 -> None
                        | _ -> 
                            let (_, last_od_range) = List.last openDirectives
                            let line = last_od_range.EndLine + 1
                            Some <| Range.mkRange file.FullName (Range.mkPos line 0) (Range.mkPos line 0)
                    | _ -> None
                    |>
                    function
                    | Some r -> r
                    | None -> Range.mkRange file.FullName (Range.mkPos 2 0) (Range.mkPos 2 0)
                    |> asRange

                let openQuickFixAction = openQuickFixAction openInsertionPoint
                for (sym: FSharpSymbolUse) in symquery do
                    let fullname = sym.Symbol.FullName
                    let partials, _ = QuickParse.GetPartialLongName(fullname, fullname.Length - 1)
                    let replaceRange = refineRange(sym.Symbol.DisplayName, file, fsRange)
                    actions.Add <| openQuickFixAction (FSharp.Core.String.concat "." partials )
                    actions.Add <| useFullyQualifiedQuickFix fullname replaceRange

                for (ent: FSharpEntity) in asmquery do
                    let replaceRange = refineRange(ent.DisplayName, file, fsRange)
                    actions.Add <| openQuickFixAction ent.AccessPath
                    actions.Add <| useFullyQualifiedQuickFix (sprintf "%s.%s" ent.AccessPath ent.DisplayName) replaceRange
                    ()

            match proj with
            | Error _ -> async { return [] }
            | Ok _ ->

            async {
                let check = checkOpenFile(file, true, false)
                let actions = ResizeArray()

                match no_name, no_binding with
                | Some symbolName, _
                | _, Some symbolName ->

                    let! chkquery = check
                    let! symquery = (this:>ILanguageServer).WorkspaceSymbols({query = symbolName})
                    let! symquery = 
                        List.filter (fun i -> i.name = symbolName) symquery
                        |> List.filter (fun i -> projects.IsVisible(FileInfo(i.location.uri.LocalPath), file))
                        |> List.map (fun {location = {uri = uri; range = range}} -> 
                            // open the target file so as to check
                            let targetFile = FileInfo(uri.LocalPath)
                            if docs.Get(targetFile).IsNone then
                                let content = getOrRead(targetFile)
                                docs.Open({textDocument={uri=uri; languageId="fsharp"; version=0; text=content.Value}})
                            symbolAt({uri = uri}, range.start))
                        |> Async.Parallel
                    let symquery = Array.choose id symquery // XXX accessibility?
                    let asmrefs = 
                        match chkquery with
                        | Ok(_, rcheck) -> rcheck.ProjectContext.GetReferencedAssemblies()
                        | _ -> []
                        |> Seq.collect (fun asm -> asm.Contents.TryGetEntities())
                        |> Seq.filter (fun ent -> ent.DisplayName = symbolName)
                        |> List.ofSeq


                    dprintfn "symbol query result: %A" symquery
                    openOrUseFullyQualifiedQuickFix p.range symquery chkquery asmrefs actions
                | _ -> ()

                if unused_declarations then
                    actions.Add { 
                        defaultQuickFixAction 
                        with title = "rename symbol to '_'"
                    }
                if no_binding.IsSome then
                    actions.Add { 
                        defaultQuickFixAction 
                        with title = "create local binding " + no_binding.Value
                    }
                if no_member.IsSome then
                    actions.Add { 
                        defaultQuickFixAction 
                        with title = "create member " + no_member.Value
                    }
                if interface_notimplemented.IsSome then
                    actions.Add { 
                        defaultQuickFixAction 
                        with title = "implement members of " + interface_notimplemented.Value
                    }
                if interface_nomember then
                    actions.Add { 
                        defaultQuickFixAction 
                        with title = "add as an interface member"
                    }

                return List.ofSeq actions
            }

        member __.CodeLens(p: CodeLensParams): Async<CodeLens list> = 
            async {
                let file = FileInfo(p.textDocument.uri.LocalPath)
                match projects.FindProjectOptions(file), getOrRead(file) with 
                | Ok(projectOptions), Some(sourceText) -> 
                    let parsingOptions = getParsingOptions(projectOptions)
                    let sourceText = SourceText.ofString sourceText
                    let! parse = checker.ParseFile(file.FullName, sourceText, parsingOptions)
                    let lenses = ResizeArray()

                    if file.Name.EndsWith(".fs") then 
                        let fsproj = FileInfo(projectOptions.ProjectFileName)
                        for id, bindings in testFunctions(parse) do 
                            lenses.Add <| asRunTest(fsproj, id, bindings)

                    if file.Name.EndsWith(".fsi") then
                        for name, range in findSignatureDeclarations(parse) do 
                            lenses.Add <| asGoToImplementation(name, file, range)

                    if codelensShowReferences then
                        for decl, inRange in findDeclarations(parse) do
                            let! nrefs = findReferenceCount(decl)
                            lenses.Add <| asReferenceCount(decl, inRange, nrefs)

                    return Seq.toList lenses
                | Error(e), _ -> 
                    dprintfn "Failed to create code lens because project options failed to load: %A" e
                    return []
                | _, None -> 
                    dprintfn "Failed to create code lens because file %s does not exist" file.FullName
                    return []
            }
        member __.ResolveCodeLens(p: CodeLens): Async<CodeLens> = 
            async {
                if p.data <> JsonValue.Null then 
                    dprintfn "Resolving %A" p.data
                    let fsi, name = goToImplementationData(p)
                    if not(fsi.Extension = ".fsi") then 
                        raise(Exception(sprintf "Signature file %s should end with .fsi" fsi.Name))
                    let file = FileInfo(fsi.FullName.Substring(0, fsi.FullName.Length - 1))
                    match projects.FindProjectOptions(file), getOrRead(file) with 
                    | Ok(projectOptions), Some(sourceText) -> 
                        let parsingOptions = getParsingOptions(projectOptions)
                        let sourceText = SourceText.ofString sourceText
                        let! parse = checker.ParseFile(file.FullName, sourceText, parsingOptions)
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
        member __.DocumentLink(p: DocumentLinkParams): Async<DocumentLink list> = TODO()
        member __.ResolveDocumentLink(p: DocumentLink): Async<DocumentLink> = TODO()

        member __.DocumentFormatting(p: DocumentFormattingParams): Async<TextEdit list> = 
          documentFormatting(p.textDocument.uri.LocalPath, None, p.options, p.optionsMap)

        member __.DocumentRangeFormatting(p: DocumentRangeFormattingParams): Async<TextEdit list> = 
          documentFormatting(p.textDocument.uri.LocalPath, Some p.range, p.options, p.optionsMap)

        member __.DocumentOnTypeFormatting(p: DocumentOnTypeFormattingParams): Async<TextEdit list> = TODO()
        member __.Rename(p: RenameParams): Async<WorkspaceEdit> =
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
        member __.ExecuteCommand(p: ExecuteCommandParams): Async<unit> = TODO()
        member __.DidChangeWorkspaceFolders(p: DidChangeWorkspaceFoldersParams): Async<unit> = 
            let __doworkspace (xs: WorkspaceFolder list) fn = 
                async {
                    for root in xs do 
                        let file = FileInfo(root.uri.LocalPath)
                        do! fn file.Directory
                }
            async {
                do! __doworkspace p.event.added projects.AddWorkspaceRoot
                do! __doworkspace p.event.removed projects.RemoveWorkspaceRoot
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
