module Main.Program

open Microsoft.FSharp.Compiler.SourceCodeServices
open System
open System.IO
open LSP
open LSP.Types

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

type private FindFile = 
    | NoSourceFile of sourcePath: string
    | NoProjectFile of sourcePath: string
    | NotInProjectOptions of sourcePath: string * projectOptions: FSharpProjectOptions 
    | Found of sourcePath: string * sourceVersion: int * sourceText: string * projectOptions: FSharpProjectOptions 

type private CheckFile = 
    | Errors of Diagnostic list
    | Ok of parseResults: FSharpParseFileResults * checkResults: FSharpCheckFileResults * errors: Diagnostic list

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
                let! parseResults, checkAnswer = checker.ParseAndCheckFileInProject(sourcePath, sourceVersion, sourceText, projectOptions)
                let parseErrors = convertDiagnostics parseResults.Errors
                match checkAnswer with 
                | FSharpCheckFileAnswer.Aborted -> 
                    eprintfn "Aborted checking %s" sourcePath 
                    return Errors parseErrors
                | FSharpCheckFileAnswer.Succeeded checkResults -> 
                    let checkErrors = convertDiagnostics checkResults.Errors 
                    let allErrors = parseErrors@checkErrors 
                    return Ok(parseResults, checkResults, allErrors)
        } |> Async.RunSynchronously
    // Check a file and send all errors to the client
    let lint (uri: Uri): unit = 
        match check uri with 
        | Errors errors -> client.PublishDiagnostics({uri=uri; diagnostics=errors})
        | Ok(parseResults, checkResults, errors) -> client.PublishDiagnostics({uri=uri; diagnostics=errors})
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
