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
        
    type Notification = 
    | Cancel of id: int 

    let parseNotification (body: JsonValue): Notification = 
        Cancel (body?id.AsInteger())

    type Position = {
        line: int
        character: int
    }

    type Range = {
        start: Position
        _end: Position
    }

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

    type VersionedTextDocumentIdentifier = {
        uri: Uri 
        version: int 
    }

    type TextDocumentEdit = {
        textDocument: VersionedTextDocumentIdentifier
        edits: list<TextEdit>
    }

    type WorkspaceEdit = {
        changes: Map<string, list<TextEdit>>
        documentChanges: list<TextDocumentEdit>
    }

    type TextDocumentIdentifier = {
        uri: Uri
    }

    type TextDocumentItem = {
        uri: Uri 
        languageId: string 
        version: int 
        text: string
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

    type Request = 
    | Initialize of 
        processId: option<int> * 
        rootUri: option<Uri> * 
        initializationOptions: option<JsonValue> *
        capabilitiesMap: Map<string, bool> *
        trace: option<Trace>

    type ExpectedResponse = ExpectedResponse of string

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
                    | JsonValue.Boolean setting -> yield (path, setting)
                    | _ -> yield! flatten newPath value
            } 
        let kvs = seq {
            for (key, value) in nested.Properties do 
                if key <> "experimental" then 
                    yield! flatten key value
        }
        Map.ofSeq kvs

    let parseInitialize (body: JsonValue): Request = 
        let processId = body?processId |> checkNull |> Option.map JsonExtensions.AsInteger
        let rootUri = body?rootUri |> checkNull |> Option.map JsonExtensions.AsString |> Option.map Uri
        let initializationOptions = body.TryGetProperty("initializationOptions") 
        let capabilities = body?capabilities |> parseCapabilities 
        let trace = body.TryGetProperty("trace") |> Option.bind checkNull |> Option.map JsonExtensions.AsString |> Option.map parseTrace
        Initialize(processId, rootUri, initializationOptions, capabilities, trace)

    let parseRequest (method: string) (body: JsonValue): Request * ExpectedResponse = 
        match method with 
        | "initialize" -> (parseInitialize body, ExpectedResponse "InitializeResult")
        | _ -> raise (Exception (sprintf "Unexpected request method %s" method))