module LSP.LanguageServer 

open FSharp.Data
open System
open System.IO
open System.Text
open Types 
open Json

let jsonWriteOptions = 
    { defaultJsonWriteOptions with 
        customWriters = 
            [ writeTextDocumentSaveReason;
              writeFileChangeType;
              writeTextDocumentSyncKind;
              writeDiagnosticSeverity;
              writeTrace;
              writeInsertTextFormat;
              writeCompletionItemKind;
              writeMarkedString;
              writeDocumentHighlightKind;
              writeSymbolKind ] }

let private serializeInitializeResult = serializerFactory<InitializeResult> jsonWriteOptions
let private serializeTextEditList = serializerFactory<TextEdit list> jsonWriteOptions
let private serializeCompletionList = serializerFactory<CompletionList> jsonWriteOptions
let private serializeHover = serializerFactory<Hover> jsonWriteOptions
let private serializeCompletionItem = serializerFactory<CompletionItem> jsonWriteOptions
let private serializeSignatureHelp = serializerFactory<SignatureHelp> jsonWriteOptions
let private serializeLocationList = serializerFactory<Location list> jsonWriteOptions
let private serializeDocumentHighlightList = serializerFactory<DocumentHighlight list> jsonWriteOptions
let private serializeSymbolInformationList = serializerFactory<SymbolInformation list> jsonWriteOptions
let private serializeCommandList = serializerFactory<Command list> jsonWriteOptions
let private serializeCodeLensList = serializerFactory<CodeLens list> jsonWriteOptions
let private serializeCodeLens = serializerFactory<CodeLens> jsonWriteOptions
let private serializeDocumentLinkList = serializerFactory<DocumentLink list> jsonWriteOptions
let private serializeDocumentLink = serializerFactory<DocumentLink> jsonWriteOptions
let private serializeWorkspaceEdit = serializerFactory<WorkspaceEdit> jsonWriteOptions
let private serializePublishDiagnostics = serializerFactory<PublishDiagnosticsParams> jsonWriteOptions

let private writeClient (client: BinaryWriter) (messageText: string) =
    let messageBytes = Encoding.UTF8.GetBytes messageText
    let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
    let headerBytes = Encoding.UTF8.GetBytes headerText
    client.Write headerBytes
    client.Write messageBytes

let respond (client: BinaryWriter) (requestId: int) (jsonText: string) = 
    let messageText = sprintf """{"id":%d,"result":%s}""" requestId jsonText
    writeClient client messageText

let notifyClient (client: BinaryWriter) (method: string) (jsonText: string) = 
    let messageText = sprintf """{"method":"%s","params":%s}""" method jsonText
    writeClient client messageText

let processRequest (server: ILanguageServer) (send: BinaryWriter) (id: int) (request: Request) = 
    match request with 
    | Initialize p -> 
        server.Initialize p |> serializeInitializeResult |> respond send id
    | WillSaveWaitUntilTextDocument p -> 
        server.WillSaveWaitUntilTextDocument p |> serializeTextEditList |> respond send id
    | Completion p -> 
        server.Completion p |> Option.map serializeCompletionList |> Option.defaultValue "null" |> respond send id
    | Hover p -> 
        server.Hover p |> Option.map serializeHover |> Option.defaultValue "null" |> respond send id
    | ResolveCompletionItem p -> 
        server.ResolveCompletionItem p |> serializeCompletionItem |> respond send id 
    | SignatureHelp p -> 
        server.SignatureHelp p |> Option.map serializeSignatureHelp |> Option.defaultValue "null" |> respond send id
    | GotoDefinition p -> 
        server.GotoDefinition p |> serializeLocationList |> respond send id
    | FindReferences p -> 
        server.FindReferences p |> serializeLocationList |> respond send id
    | DocumentHighlight p -> 
        server.DocumentHighlight p |> serializeDocumentHighlightList |> respond send id
    | DocumentSymbols p -> 
        server.DocumentSymbols p |> serializeSymbolInformationList |> respond send id
    | WorkspaceSymbols p -> 
        server.WorkspaceSymbols p |> serializeSymbolInformationList |> respond send id
    | CodeActions p -> 
        server.CodeActions p |> serializeCommandList |> respond send id
    | CodeLens p -> 
        server.CodeLens p |> serializeCodeLensList |> respond send id
    | ResolveCodeLens p -> 
        server.ResolveCodeLens p |> serializeCodeLens |> respond send id
    | DocumentLink p -> 
        server.DocumentLink p |> serializeDocumentLinkList |> respond send id
    | ResolveDocumentLink p -> 
        server.ResolveDocumentLink p |> serializeDocumentLink |> respond send id
    | DocumentFormatting p -> 
        server.DocumentFormatting p |> serializeTextEditList |> respond send id
    | DocumentRangeFormatting p -> 
        server.DocumentRangeFormatting p |> serializeTextEditList |> respond send id
    | DocumentOnTypeFormatting p -> 
        server.DocumentOnTypeFormatting p |> serializeTextEditList |> respond send id
    | Rename p -> 
        server.Rename p |> serializeWorkspaceEdit |> respond send id
    | ExecuteCommand p -> 
        server.ExecuteCommand p 

let processNotification (server: ILanguageServer) (send: BinaryWriter) (n: Notification) = 
    match n with 
    | Cancel id ->
        eprintfn "Cancel request %d is not yet supported" id
    | Initialized ->
        server.Initialized()
    | Shutdown ->
        server.Shutdown()
    | DidChangeConfiguration p -> 
        server.DidChangeConfiguration p
    | DidOpenTextDocument p -> 
        server.DidOpenTextDocument p
    | DidChangeTextDocument p -> 
        server.DidChangeTextDocument p
    | WillSaveTextDocument p -> 
        server.WillSaveTextDocument p 
    | DidSaveTextDocument p -> 
        server.DidSaveTextDocument p
    | DidCloseTextDocument p -> 
        server.DidCloseTextDocument p
    | DidChangeWatchedFiles p -> 
        server.DidChangeWatchedFiles p
    | OtherNotification _ ->
        ()

let processMessage (server: ILanguageServer) (send: BinaryWriter) (m: Parser.Message) = 
    match m with 
    | Parser.RequestMessage (id, method, json) -> 
        processRequest server send id (Parser.parseRequest method json) 
    | Parser.NotificationMessage (method, json) -> 
        processNotification server send (Parser.parseNotification method json)

let private notExit (message: Parser.Message) = 
    match message with 
    | Parser.NotificationMessage ("exit", _) -> false 
    | _ -> true

let readMessages (receive: BinaryReader): seq<Parser.Message> = 
    Tokenizer.tokenize receive |> Seq.map Parser.parseMessage |> Seq.takeWhile notExit

type RealClient (send: BinaryWriter) = 
    interface ILanguageClient with 
        member this.PublishDiagnostics (p: PublishDiagnosticsParams): unit = 
            p |> serializePublishDiagnostics |> notifyClient send "textDocument/publishDiagnostics"

let connect (serverFactory: ILanguageClient -> ILanguageServer) (receive: BinaryReader) (send: BinaryWriter) = 
    let server = serverFactory(RealClient(send))
    let doProcessMessage = processMessage server send 
    readMessages receive |> Seq.iter doProcessMessage