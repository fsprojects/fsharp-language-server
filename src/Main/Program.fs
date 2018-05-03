module Main.Program

open LSP
open LSP.Types
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices

let private TODO() = raise (Exception "TODO")

type Server() = 
    let docs = DocumentStore()
    let projects = ProjectManager()
    let checker = FSharpChecker.Create()
    let emptyProjectOptions = checker.GetProjectOptionsFromCommandLineArgs("NotFound.fsproj", [||])
    let notFound (doc: Uri) (): 'Any = 
        raise (Exception (sprintf "%s does not exist" (doc.ToString())))
    let lint (doc: Uri): Async<unit> = 
        async {
            let name = doc.AbsolutePath.ToString()
            let version = docs.GetVersion doc |> Option.defaultWith (notFound doc)
            let source = docs.GetText doc |> Option.defaultWith (notFound doc)
            let projectOptions = projects.FindProjectOptions doc |> Option.defaultValue emptyProjectOptions
            let! parseResults, checkAnswer = checker.ParseAndCheckFileInProject(name, version, source, projectOptions)
            for error in parseResults.Errors do 
                eprintfn "%s %d:%d %s" error.FileName error.StartLineAlternate error.StartColumn error.Message
            match checkAnswer with 
            | FSharpCheckFileAnswer.Aborted -> eprintfn "Aborted checking %s" name 
            | FSharpCheckFileAnswer.Succeeded checkResults -> 
                for error in checkResults.Errors do 
                    eprintfn "%s %d:%d %s" error.FileName error.StartLineAlternate error.StartColumn error.Message
        }
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
            lint p.textDocument.uri |> Async.RunSynchronously
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams): unit = 
            docs.Change p
            lint p.textDocument.uri |> Async.RunSynchronously
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams): unit = TODO()
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): list<TextEdit> = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams): unit = 
            eprintfn "%s" (p.ToString())
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams): unit = 
            docs.Close p
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): unit = 
            for change in p.changes do 
                eprintfn "Watched file %s %s" (change.uri.ToString()) (change._type.ToString())
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
    let server = Server()
    eprintfn "Listening on stdin"
    LanguageServer.connect server read write
    0 // return an integer exit code
