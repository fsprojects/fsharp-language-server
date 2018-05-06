module Main.Program

open Microsoft.FSharp.Compiler.SourceCodeServices
open System
open System.IO
open LSP
open LSP.Types

let private TODO() = raise (Exception "TODO")

let private asRange (err: FSharpErrorInfo): Range = 
    {
        // Got error "The field, constructor or member 'StartLine' is not defined"
        start = {line=err.StartLineAlternate-1; character=err.StartColumn}
        ``end`` = {line=err.EndLineAlternate-1; character=err.EndColumn}
    }

let private hasNoLocation (err: FSharpErrorInfo): bool = 
    err.StartLineAlternate-1 = 0 && 
    err.StartColumn = 0 &&
    err.EndLineAlternate-1 = 0 &&
    err.EndColumn = 0

let private asDiagnosticSeverity(s: FSharpErrorSeverity): DiagnosticSeverity =
    match s with 
    | FSharpErrorSeverity.Warning -> DiagnosticSeverity.Warning 
    | FSharpErrorSeverity.Error -> DiagnosticSeverity.Error 

let private asDiagnostic (err: FSharpErrorInfo): Diagnostic = 
    {
        range = asRange(err)
        severity = Some (asDiagnosticSeverity err.Severity)
        code = Some (sprintf "%d: %s" err.ErrorNumber err.Subcategory)
        source = None
        message = err.Message
    }
let private alreadyLogged = System.Collections.Generic.HashSet<string>()
let logOnce (message: string): unit = 
    if not (alreadyLogged.Contains message) then 
        eprintfn "%s" message 
        alreadyLogged.Add(message) |> ignore
let convertDiagnostics (uri: Uri) (errors: FSharpErrorInfo[]): PublishDiagnosticsParams =
    {
        uri = uri 
        diagnostics = 
            [ 
                for err in errors do 
                    if hasNoLocation err then 
                        logOnce(sprintf "NOPOS %s %d %s '%s'" err.FileName err.ErrorNumber err.Subcategory err.Message)
                    else
                        yield asDiagnostic(err) 
            ]
    }
let private notInProjectFile (uri: Uri) (projectFile: FileInfo option): PublishDiagnosticsParams =
    {
        uri = uri 
        diagnostics =
            [{
                range = { start = {line=0; character=0}; ``end`` = {line=0; character=1} }
                severity = Some DiagnosticSeverity.Error 
                code = None
                source = None 
                message = projectFile |> Option.map (fun f -> sprintf "Not in project %s" f.Name) |> Option.defaultValue "No .fsproj file"
            }]
    }

type Server(client: ILanguageClient) = 
    let docs = DocumentStore()
    let projects = ProjectManager()
    let checker = FSharpChecker.Create()
    let emptyProjectOptions = checker.GetProjectOptionsFromCommandLineArgs("NotFound.fsproj", [||])
    let notFound (uri: Uri) (): 'Any = 
        raise (Exception (sprintf "%s does not exist" (uri.ToString())))
    let publishDiagnostics (uri: Uri) (errors: FSharpErrorInfo[]) = 
        let lspErrors = convertDiagnostics uri errors
        client.PublishDiagnostics lspErrors
    let lint (uri: Uri): unit = 
        async {
            eprintfn "Lint %O" uri
            let sourceFile = uri.AbsolutePath.ToString()
            let version = docs.GetVersion uri |> Option.defaultWith (notFound uri)
            let source = docs.GetText uri |> Option.defaultWith (notFound uri)
            let projectFile = projects.FindProjectFile uri
            let projectOptions = Option.map projects.FindProjectOptions projectFile |> Option.defaultValue emptyProjectOptions
            if Array.contains sourceFile projectOptions.SourceFiles then 
                let! parseResults, checkAnswer = checker.ParseAndCheckFileInProject(sourceFile, version, source, projectOptions)
                publishDiagnostics uri parseResults.Errors
                match checkAnswer with 
                | FSharpCheckFileAnswer.Aborted -> eprintfn "Aborted checking %s" sourceFile 
                | FSharpCheckFileAnswer.Succeeded checkResults -> 
                        publishDiagnostics uri checkResults.Errors
            else 
                client.PublishDiagnostics(notInProjectFile uri projectFile)
        } |> Async.RunSynchronously
    interface ILanguageServer with 
        member this.Initialize(p: InitializeParams): InitializeResult = 
            { capabilities = 
                { defaultServerCapabilities with 
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
                if change.uri.AbsolutePath.EndsWith ".fsproj" then
                    projects.UpdateProjectFile change.uri 
        member this.Completion(p: TextDocumentPositionParams): CompletionList = TODO()
        member this.Hover(p: TextDocumentPositionParams): Hover = TODO()
        member this.ResolveCompletionItem(p: CompletionItem): CompletionItem = TODO()
        member this.SignatureHelp(p: TextDocumentPositionParams): SignatureHelp = TODO()
        member this.GotoDefinition(p: TextDocumentPositionParams): list<Location> = TODO()
        member this.FindReferences(p: ReferenceParams): list<Location> = TODO()
        member this.DocumentHighlight(p: TextDocumentPositionParams): list<DocumentHighlight> = TODO()
        member this.DocumentSymbols(p: DocumentSymbolParams): list<SymbolInformation> = TODO()
        member this.WorkspaceSymbols(p: WorkspaceSymbolParams): list<SymbolInformation> = TODO()
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
