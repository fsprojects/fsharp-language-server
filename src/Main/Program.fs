// Learn more about F# at http://fsharp.org

open LSP
open LSP.Types
open System
open System.IO

let TODO() = raise (Exception "TODO")

type Server() = 
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
        member this.Exit(): unit = 
            ()
        member this.DidChangeConfiguration(p: DidChangeConfigurationParams): unit =
            Log.info "New configuration %s" (p.ToString())
        member this.DidOpenTextDocument(p: DidOpenTextDocumentParams): unit = 
            Log.info "%s" (p.ToString())
        member this.DidChangeTextDocument(p: DidChangeTextDocumentParams): unit = 
            Log.info "%s" (p.ToString())
        member this.WillSaveTextDocument(p: WillSaveTextDocumentParams): unit = TODO()
        member this.WillSaveWaitUntilTextDocument(p: WillSaveTextDocumentParams): list<TextEdit> = TODO()
        member this.DidSaveTextDocument(p: DidSaveTextDocumentParams): unit = 
            Log.info "%s" (p.ToString())
        member this.DidCloseTextDocument(p: DidCloseTextDocumentParams): unit = 
            Log.info "%s" (p.ToString())
        member this.DidChangeWatchedFiles(p: DidChangeWatchedFilesParams): unit = TODO()
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
    Log.info "Listening on stdin"
    LanguageServer.connect server read write
    0 // return an integer exit code
