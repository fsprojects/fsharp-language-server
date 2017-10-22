namespace LSP

open System
open FSharp.Data
open FSharp.Data.JsonExtensions

module Parser = 
    type Message = 
    | RequestMessage of id: int * method: string * body: option<JsonValue>
    | NotificationMessage of method: string * body: option<JsonValue>

    let parseMessage (jsonText: string): Message = 
        let json = JsonValue.Parse jsonText
        let jsonRpcVersion = json?jsonrpc.AsString()
        assert (jsonRpcVersion = "2.0")
        let maybeId = json.TryGetProperty("id") |> Option.map JsonExtensions.AsInteger
        let method = json?method.AsString()
        let body = json.TryGetProperty("params")

        match maybeId with
        | Some id -> RequestMessage (id, method, body)
        | None -> NotificationMessage (method, body)

    type MessageType = 
    | Error
    | Warning 
    | Info 
    | Log

    type ShowMessageParams = {
        _type: MessageType 
        message: string
    }

    type DidChangeConfigurationParams = {
        settings: JsonValue
    }

    type TextDocumentItem = {
        uri: Uri 
        languageId: string 
        version: int 
        text: string
    }

    type DidOpenTextDocumentParams = {
        textDocument: TextDocumentItem
    }

    type VersionedTextDocumentIdentifier = {
        uri: Uri 
        version: int 
    }

    type Position = {
        line: int
        character: int
    }

    type Range = {
        start: Position
        _end: Position
    }

    type TextDocumentContentChangeEvent = {
        range: option<Range>
        rangeLength: option<int>
        text: string
    }

    type DidChangeTextDocumentParams = {
        textDocument: VersionedTextDocumentIdentifier
        contentChanges: list<TextDocumentContentChangeEvent>
    }

    type TextDocumentIdentifier = {
        uri: Uri
    }

    type TextDocumentSaveReason = 
    | Manual
    | AfterDelay
    | FocusOut

    type WillSaveTextDocumentParams = {
        textDocument: TextDocumentIdentifier
        reason: TextDocumentSaveReason
    }

    type DidSaveTextDocumentParams = {
        textDocument: TextDocumentIdentifier
        text: option<string>
    }

    type DidCloseTextDocumentParams = {
        textDocument: TextDocumentIdentifier
    }

    type FileChangeType = 
    | Created
    | Changed 
    | Deleted

    type FileEvent = {
        uri: Uri 
        _type: FileChangeType
    }

    type DidChangeWatchedFilesParams = {
        changes: list<FileEvent>
    }
        
    type Notification = 
    | Cancel of id: int 
    | Initialized
    | Shutdown 
    | Exit 
    | DidChangeConfiguration of DidChangeConfigurationParams
    | DidOpenTextDocument of DidOpenTextDocumentParams
    | DidChangeTextDocument of DidChangeTextDocumentParams
    | WillSaveTextDocument of WillSaveTextDocumentParams
    | WillSaveWaitUntilTextDocument of WillSaveTextDocumentParams
    | DidSaveTextDocument of DidSaveTextDocumentParams
    | DidCloseTextDocument of DidCloseTextDocumentParams
    | DidChangeWatchedFiles of DidChangeWatchedFilesParams

    let parseMessageType (id: int): MessageType = 
        match id with 
        | 1 -> Error 
        | 2 -> Warning 
        | 3 -> Info 
        | 4 -> Log

    let parseDidChangeConfigurationParams (body: JsonValue): DidChangeConfigurationParams = 
        {
            settings = body?settings
        }

    let parseTextDocumentItem (json: JsonValue): TextDocumentItem = 
        {
            uri = Uri(json?uri.AsString())
            languageId = json?languageId.AsString()
            version = json?version.AsInteger()
            text = json?text.AsString()
        }

    let parseDidOpenTextDocumentParams (body: JsonValue): DidOpenTextDocumentParams = 
        {
            textDocument = body?textDocument |> parseTextDocumentItem 
        }

    let parsePosition (json: JsonValue): Position = 
        {
            line = json?line.AsInteger()
            character = json?character.AsInteger()
        }

    let parseRange (json: JsonValue): Range = 
        {
            start = json?start |> parsePosition 
            _end = json?``end`` |> parsePosition
        }

    let parseVersionedTextDocumentIdentifier (json: JsonValue): VersionedTextDocumentIdentifier = 
        {
            uri = json?uri.AsString() |> Uri 
            version = json?version.AsInteger()
        }

    let parseTextDocumentContentChangeEvent (json: JsonValue): TextDocumentContentChangeEvent = 
        {
            range = json.TryGetProperty("range") |> Option.map parseRange
            rangeLength = json.TryGetProperty("rangeLength") |> Option.map JsonExtensions.AsInteger 
            text = json?text.AsString()
        }
    
    let parseDidChangeTextDocumentParams (json: JsonValue): DidChangeTextDocumentParams = 
        {
            textDocument = json?textDocument |> parseVersionedTextDocumentIdentifier
            contentChanges = json?contentChanges.AsArray() |> List.ofArray |> List.map parseTextDocumentContentChangeEvent
        }

    let parseTextDocumentIdentifier (json: JsonValue): TextDocumentIdentifier = 
        {
            uri = json?uri.AsString() |> Uri
        }

    let parseTextDocumentSaveReason (i: int): TextDocumentSaveReason = 
        match i with 
        | 1 -> Manual 
        | 2 -> AfterDelay 
        | 3 -> FocusOut

    let parseWillSaveTextDocumentParams (json: JsonValue): WillSaveTextDocumentParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            reason = json?reason.AsInteger() |> parseTextDocumentSaveReason
        }

    let parseDidSaveTextDocumentParams (json: JsonValue): DidSaveTextDocumentParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            text = json.TryGetProperty("text") |> Option.map JsonExtensions.AsString
        }

    let parseDidCloseTextDocumentParams (json: JsonValue): DidCloseTextDocumentParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
        }

    let parseFileChangeType (i: int): FileChangeType = 
        match i with 
        | 1 -> Created 
        | 2 -> Changed 
        | 3 -> Deleted

    let parseFileEvent (json: JsonValue): FileEvent = 
        {
            uri = json?uri.AsString() |> Uri 
            _type = json?``type``.AsInteger() |> parseFileChangeType
        }

    let parseDidChangeWatchedFilesParams (json: JsonValue): DidChangeWatchedFilesParams = 
        {
            changes = json?changes.AsArray() |> List.ofArray |> List.map parseFileEvent
        }

    let parseNotification (method: string) (maybeBody: option<JsonValue>): Notification = 
        match method, maybeBody with 
        | "cancel", Some body -> Cancel (body?id.AsInteger())
        | "initialized", None -> Initialized
        | "shutdown", None -> Shutdown 
        | "exit", None -> Exit 
        | "workspace/didChangeConfiguration", Some body -> DidChangeConfiguration (parseDidChangeConfigurationParams body)
        | "textDocument/didOpen", Some body -> DidOpenTextDocument (parseDidOpenTextDocumentParams body)
        | "textDocument/didChange", Some body -> DidChangeTextDocument (parseDidChangeTextDocumentParams body)
        | "textDocument/willSave", Some body -> WillSaveTextDocument (parseWillSaveTextDocumentParams body)
        | "textDocument/willSaveWaitUntil", Some body -> WillSaveWaitUntilTextDocument (parseWillSaveTextDocumentParams body)
        | "textDocument/didSave", Some body -> DidSaveTextDocument (parseDidSaveTextDocumentParams body)
        | "textDocument/didClose", Some body -> DidCloseTextDocument (parseDidCloseTextDocumentParams body)
        | "workspace/didChangeWatchedFiles", Some body -> DidChangeWatchedFiles (parseDidChangeWatchedFilesParams body)

    type Location = {
        uri: Uri 
        range: Range 
    }

    type DiagnosticSeverity = Error | Warning | Information | Hint

    type Diagnostic = {
        range: Range
        severity: option<DiagnosticSeverity>
        code: option<string>
        source: option<string>
        message: string;
    }

    type Command = {
        title: string
        command: string 
        arguments: list<JsonValue>
    }

    type TextEdit = {
        range: Range 
        newText: string
    }

    type TextDocumentEdit = {
        textDocument: VersionedTextDocumentIdentifier
        edits: list<TextEdit>
    }

    type WorkspaceEdit = {
        changes: Map<string, list<TextEdit>>
        documentChanges: list<TextDocumentEdit>
    }

    type TextDocumentPositionParams = {
        textDocument: TextDocumentIdentifier
        position: Position
    }

    type DocumentFilter = {
        language: string 
        scheme: string 
        pattern: string
    }

    type DocumentSelector = list<DocumentFilter>

    type Trace = Off | Messages | Verbose

    type InitializeParams = {
        processId: option<int>
        rootUri: option<Uri>
        initializationOptions: option<JsonValue>
        capabilitiesMap: Map<string, bool>
        trace: option<Trace>
    }

    type Request = 
    | Initialize of InitializeParams

    let checkNull (json: JsonValue): option<JsonValue> = 
        match json with 
        | JsonValue.Null -> None 
        | _ -> Some json 

    let parseTrace (text: string): Trace = 
        match text with 
        | "off" -> Off 
        | "messages" -> Messages 
        | "verbose" -> Verbose
        | _ -> raise (Exception (sprintf "Unexpected trace %s" text))

    let pathString (pathReversed: list<string>): string = 
        String.concat "." (List.rev pathReversed)

    let parseCapabilities (nested: JsonValue): Map<string, bool> =
        let rec flatten (path: string) (node: JsonValue) = 
            seq {
                for (key, value) in node.Properties do 
                    let newPath = path + "." + key
                    match value with 
                    | JsonValue.Boolean setting -> yield (newPath, setting)
                    | _ -> yield! flatten newPath value
            } 
        let kvs = seq {
            for (key, value) in nested.Properties do 
                if key <> "experimental" then 
                    yield! flatten key value
        }
        Map.ofSeq kvs

    let parseInitialize (body: JsonValue): InitializeParams = 
        { 
             processId = body?processId |> checkNull |> Option.map JsonExtensions.AsInteger
             rootUri = body?rootUri |> checkNull |> Option.map JsonExtensions.AsString |> Option.map Uri
             initializationOptions = body.TryGetProperty("initializationOptions") 
             capabilitiesMap = body?capabilities |> parseCapabilities 
             trace = body.TryGetProperty("trace") |> Option.bind checkNull |> Option.map JsonExtensions.AsString |> Option.map parseTrace
        }

    let parseRequest (method: string) (body: JsonValue): Request = 
        match method with 
        | "initialize" -> Initialize (parseInitialize body)
        | _ -> raise (Exception (sprintf "Unexpected request method %s" method))