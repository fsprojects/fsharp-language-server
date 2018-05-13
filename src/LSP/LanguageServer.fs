module LSP.LanguageServer 

open System.Collections
open System
open System.Threading
open System.IO
open System.Text
open FSharp.Data
open Types 
open Json

let private jsonWriteOptions = 
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
              writeSymbolKind;
              writeRegisterCapability ] }

let private serializeInitializeResult = serializerFactory<InitializeResult> jsonWriteOptions
let private serializeTextEditList = serializerFactory<TextEdit list> jsonWriteOptions
let private serializeCompletionList = serializerFactory<CompletionList> jsonWriteOptions
let private serializeCompletionListOption = Option.map serializeCompletionList
let private serializeHover = serializerFactory<Hover> jsonWriteOptions
let private serializeHoverOption = Option.map serializeHover
let private serializeCompletionItem = serializerFactory<CompletionItem> jsonWriteOptions
let private serializeSignatureHelp = serializerFactory<SignatureHelp> jsonWriteOptions
let private serializeSignatureHelpOption = Option.map serializeSignatureHelp
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
let private serializeRegistrationParams = serializerFactory<RegistrationParams> jsonWriteOptions

let private writeClient (client: BinaryWriter) (messageText: string) =
    let messageBytes = Encoding.UTF8.GetBytes messageText
    let headerText = sprintf "Content-Length: %d\r\n\r\n" messageBytes.Length
    let headerBytes = Encoding.UTF8.GetBytes headerText
    client.Write headerBytes
    client.Write messageBytes

let private respond (client: BinaryWriter) (requestId: int) (jsonText: string) = 
    let messageText = sprintf """{"id":%d,"result":%s}""" requestId jsonText
    writeClient client messageText

let private notifyClient (client: BinaryWriter) (method: string) (jsonText: string) = 
    let messageText = sprintf """{"method":"%s","params":%s}""" method jsonText
    writeClient client messageText

let private thenMap (f: 'A -> 'B) (result: Async<'A>): Async<'B> =
    async {
        let! a = result 
        return f a
    }
let private thenSome = thenMap Some
let private thenNone(result: Async<'A>): Async<string option> = result |> thenMap (fun _ -> None)

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
        member this.RegisterCapability (p: RegisterCapability): unit = 
            eprintfn "Register capability %A" p
            match p with 
            | RegisterCapability.DidChangeWatchedFiles _ -> 
                let register = {id=Guid.NewGuid().ToString(); method="workspace/didChangeWatchedFiles"; registerOptions=p}
                let message = {registrations=[register]}
                message |> serializeRegistrationParams |> notifyClient send "client/registerCapability"

let connect (serverFactory: ILanguageClient -> ILanguageServer) (receive: BinaryReader) (send: BinaryWriter) = 
    let server = serverFactory(RealClient(send))
    let pendingRequests = System.Collections.Concurrent.ConcurrentDictionary<int, CancellationTokenSource>()
    let processRequest (request: Request): Async<string option> = 
        match request with 
        | Initialize p -> 
            server.Initialize p |> thenMap serializeInitializeResult |> thenSome
        | WillSaveWaitUntilTextDocument p -> 
            server.WillSaveWaitUntilTextDocument p |> thenMap serializeTextEditList |> thenSome
        | Completion p -> 
            server.Completion p |> thenMap serializeCompletionListOption
        | Hover p -> 
            server.Hover p |> thenMap serializeHoverOption |> thenMap (Option.defaultValue "null") |> thenSome
        | ResolveCompletionItem p -> 
            server.ResolveCompletionItem p |> thenMap serializeCompletionItem |> thenSome 
        | SignatureHelp p -> 
            server.SignatureHelp p |> thenMap serializeSignatureHelpOption |> thenMap (Option.defaultValue "null") |> thenSome
        | GotoDefinition p -> 
            server.GotoDefinition p |> thenMap serializeLocationList |> thenSome
        | FindReferences p -> 
            server.FindReferences p |> thenMap serializeLocationList |> thenSome
        | DocumentHighlight p -> 
            server.DocumentHighlight p |> thenMap serializeDocumentHighlightList |> thenSome
        | DocumentSymbols p -> 
            server.DocumentSymbols p |> thenMap serializeSymbolInformationList |> thenSome
        | WorkspaceSymbols p -> 
            server.WorkspaceSymbols p |> thenMap serializeSymbolInformationList |> thenSome
        | CodeActions p -> 
            server.CodeActions p |> thenMap serializeCommandList |> thenSome
        | CodeLens p -> 
            server.CodeLens p |> thenMap serializeCodeLensList |> thenSome
        | ResolveCodeLens p -> 
            server.ResolveCodeLens p |> thenMap serializeCodeLens |> thenSome
        | DocumentLink p -> 
            server.DocumentLink p |> thenMap serializeDocumentLinkList |> thenSome
        | ResolveDocumentLink p -> 
            server.ResolveDocumentLink p |> thenMap serializeDocumentLink |> thenSome
        | DocumentFormatting p -> 
            server.DocumentFormatting p |> thenMap serializeTextEditList |> thenSome
        | DocumentRangeFormatting p -> 
            server.DocumentRangeFormatting p |> thenMap serializeTextEditList |> thenSome
        | DocumentOnTypeFormatting p -> 
            server.DocumentOnTypeFormatting p |> thenMap serializeTextEditList |> thenSome
        | Rename p -> 
            server.Rename p |> thenMap serializeWorkspaceEdit |> thenSome
        | ExecuteCommand p -> 
            server.ExecuteCommand p |> thenNone
        | DidChangeWorkspaceFolders p ->
            server.DidChangeWorkspaceFolders p 
            async { return None }
    let processNotification (n: Notification) = 
        match n with 
        | Cancel id ->
            let stillRunning, pendingRequest = pendingRequests.TryGetValue(id)
            if stillRunning then
                eprintfn "Cancelling request %d" id
                pendingRequest.Cancel()
            else 
                eprintfn "Request %d has already finished" id
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
    let processMessage (m: Parser.Message) = 
        match m with 
        | Parser.RequestMessage (id, method, json) -> 
            let cancel = new CancellationTokenSource()
            let task = processRequest (Parser.parseRequest method json) 
            let finish = task |> thenMap (fun r -> 
                match r with
                | Some m -> respond send id m 
                | None -> ()
                pendingRequests.TryRemove(id) |> ignore
            )
            Async.Start(finish, cancel.Token)
            pendingRequests.[id] <- cancel
        | Parser.NotificationMessage (method, json) -> 
            processNotification (Parser.parseNotification method json)
    for m in readMessages receive do 
        processMessage m
    