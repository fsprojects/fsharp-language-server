module LSP.LanguageServer 

open FSharp.Data
open System
open System.IO
open System.Text
open Types 
open Json

let private serializeInitializeResult = serializerFactory<InitializeResult>()
let private serializeTextEditList = serializerFactory<list<TextEdit>>()
let private serializeCompletionList = serializerFactory<CompletionList>()
let private serializeHover = serializerFactory<Hover>()
let private serializeCompletionItem = serializerFactory<CompletionItem>()
let private serializeSignatureHelp = serializerFactory<SignatureHelp>()
let private serializeLocationList = serializerFactory<list<Location>>()
let private serializeDocumentHighlightList = serializerFactory<list<DocumentHighlight>>()
let private serializeSymbolInformationList = serializerFactory<list<SymbolInformation>>()
let private serializeCommandList = serializerFactory<list<Command>>()
let private serializeCodeLensList = serializerFactory<list<CodeLens>>()
let private serializeCodeLens = serializerFactory<CodeLens>()
let private serializeDocumentLinkList = serializerFactory<list<DocumentLink>>()
let private serializeDocumentLink = serializerFactory<DocumentLink>()
let private serializeWorkspaceEdit = serializerFactory<WorkspaceEdit>()

let respond (client: BinaryWriter) (requestId: int) (jsonText: string) = 
    let messageText = sprintf """{"id":%d,"result":%s}""" requestId jsonText
    let messageBytes = Encoding.UTF8.GetBytes messageText
    let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
    let headerBytes = Encoding.UTF8.GetBytes headerText
    client.Write headerBytes
    client.Write messageBytes

let processRequest (server: ILanguageServer) (send: BinaryWriter) (id: int) (request: Request) = 
    match request with 
    | Initialize p -> 
        server.Initialize p |> serializeInitializeResult |> respond send id
    | WillSaveWaitUntilTextDocument p -> 
        server.WillSaveWaitUntilTextDocument p |> serializeTextEditList |> respond send id
    | Completion p -> 
        server.Completion p |> serializeCompletionList |> respond send id
    | Hover p -> 
        server.Hover p |> serializeHover |> respond send id
    | ResolveCompletionItem p -> 
        server.ResolveCompletionItem p |> serializeCompletionItem |> respond send id 
    | SignatureHelp p -> 
        server.SignatureHelp p |> serializeSignatureHelp |> respond send id
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
    | Exit ->
        server.Exit()
        raise (Exception "Exited normally")
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

let processMessage (server: ILanguageServer) (send: BinaryWriter) (m: Parser.Message) = 
    match m with 
    | Parser.RequestMessage (id, method, json) -> 
        eprintf "Request %d %s" id method
        processRequest server send id (Parser.parseRequest method json) 
    | Parser.NotificationMessage (method, json) -> 
        eprintf "Notify %s" method
        processNotification server send (Parser.parseNotification method json)

let readMessages (receive: BinaryReader): seq<Parser.Message> = 
    Tokenizer.tokenize receive |> Seq.map Parser.parseMessage

let connect (server: ILanguageServer) (receive: BinaryReader) (send: BinaryWriter) = 
    let doProcessMessage = processMessage server send 
    readMessages receive |> Seq.iter doProcessMessage