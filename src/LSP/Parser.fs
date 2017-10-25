namespace LSP

open System
open FSharp.Data
open FSharp.Data.JsonExtensions
open Types

module Parser = 
    type Message = 
    | RequestMessage of id: int * method: string * json: option<JsonValue>
    | NotificationMessage of method: string * json: option<JsonValue>

    let parseMessage (jsonText: string): Message = 
        let json = JsonValue.Parse jsonText
        let jsonRpcVersion = json?jsonrpc.AsString()
        assert (jsonRpcVersion = "2.0")
        let maybeId = json.TryGetProperty("id") |> Option.map JsonExtensions.AsInteger
        let method = json?method.AsString()
        let json = json.TryGetProperty("params")

        match maybeId with
        | Some id -> RequestMessage (id, method, json)
        | None -> NotificationMessage (method, json)

    let parseDidChangeConfigurationParams (json: JsonValue): DidChangeConfigurationParams = 
        {
            settings = json?settings
        }

    let parseTextDocumentItem (json: JsonValue): TextDocumentItem = 
        {
            uri = Uri(json?uri.AsString())
            languageId = json?languageId.AsString()
            version = json?version.AsInteger()
            text = json?text.AsString()
        }

    let parseDidOpenTextDocumentParams (json: JsonValue): DidOpenTextDocumentParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentItem 
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
        | "cancel", Some json -> Cancel (json?id.AsInteger())
        | "initialized", None -> Initialized
        | "shutdown", None -> Shutdown 
        | "exit", None -> Exit 
        | "workspace/didChangeConfiguration", Some json -> DidChangeConfiguration (parseDidChangeConfigurationParams json)
        | "textDocument/didOpen", Some json -> DidOpenTextDocument (parseDidOpenTextDocumentParams json)
        | "textDocument/didChange", Some json -> DidChangeTextDocument (parseDidChangeTextDocumentParams json)
        | "textDocument/willSave", Some json -> WillSaveTextDocument (parseWillSaveTextDocumentParams json)
        | "textDocument/didSave", Some json -> DidSaveTextDocument (parseDidSaveTextDocumentParams json)
        | "textDocument/didClose", Some json -> DidCloseTextDocument (parseDidCloseTextDocumentParams json)
        | "workspace/didChangeWatchedFiles", Some json -> DidChangeWatchedFiles (parseDidChangeWatchedFilesParams json)

    type Request = 
    | Initialize of InitializeParams
    | WillSaveWaitUntilTextDocument of WillSaveTextDocumentParams
    | Completion of TextDocumentPositionParams
    | Hover of TextDocumentPositionParams
    | ResolveCompletionItem of CompletionItem
    | SignatureHelp of TextDocumentPositionParams
    | GotoDefinition of TextDocumentPositionParams
    | FindReferences of ReferenceParams
    | DocumentHighlight of TextDocumentPositionParams
    | DocumentSymbols of DocumentSymbolParams
    | WorkspaceSymbols of WorkspaceSymbolParams
    | CodeActions of CodeActionParams
    | CodeLens of CodeLensParams
    | ResolveCodeLens of CodeLens
    | DocumentLink of DocumentLinkParams
    | ResolveDocumentLink of DocumentLink
    | DocumentFormatting of DocumentFormattingParams
    | DocumentRangeFormatting of DocumentRangeFormattingParams
    | DocumentOnTypeFormatting of DocumentOnTypeFormattingParams
    | Rename of RenameParams
    | ExecuteCommand of ExecuteCommandParams

    let noneAs<'T> (orDefault: 'T) (maybe: option<'T>): 'T = 
        match maybe with 
        | Some value -> value 
        | None -> orDefault

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

    let parseInitialize (json: JsonValue): InitializeParams = 
        { 
             processId = json?processId |> checkNull |> Option.map JsonExtensions.AsInteger
             rootUri = json?rootUri |> checkNull |> Option.map JsonExtensions.AsString |> Option.map Uri
             initializationOptions = json.TryGetProperty("initializationOptions") 
             capabilitiesMap = json?capabilities |> parseCapabilities 
             trace = json.TryGetProperty("trace") |> Option.bind checkNull |> Option.map JsonExtensions.AsString |> Option.map parseTrace
        }

    let parseTextDocumentPositionParams (json: JsonValue): TextDocumentPositionParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            position = json?position |> parsePosition
        }

    let parseCompletionItemKind (i: int): CompletionItemKind = 
        match i with 
        | 1 -> Text
        | 2 -> Method
        | 3 -> Function
        | 4 -> Constructor
        | 5 -> Field
        | 6 -> Variable
        | 7 -> Class
        | 8 -> Interface
        | 9 -> Module
        | 10 -> Property
        | 11 -> Unit
        | 12 -> Value
        | 13 -> Enum
        | 14 -> Keyword
        | 15 -> Snippet
        | 16 -> Color
        | 17 -> File
        | 18 -> Reference

    let parseInsertTextFormat (i: int): InsertTextFormat = 
        match i with 
        | 1 -> InsertTextFormat.PlainText
        | 2 -> InsertTextFormat.Snippet

    let parseTextEdit (json: JsonValue): TextEdit = 
        {
            range = json?range |> parseRange
            newText = json?newText.AsString()
        }

    let parseCommand (json: JsonValue): Command = 
        {
            title = json?title.AsString()
            command = json?command.AsString()
            arguments = json.TryGetProperty("arguments") |> Option.map JsonExtensions.AsArray |> noneAs [||] |> List.ofArray
        }

    let parseCompletionItem (json: JsonValue): CompletionItem = 
        {
            label = json?label.AsString()
            kind = json.TryGetProperty("kind") |> Option.map JsonExtensions.AsInteger |> Option.map parseCompletionItemKind
            detail = json.TryGetProperty("detail") |> Option.map JsonExtensions.AsString 
            documentation = json.TryGetProperty("documentation") |> Option.map JsonExtensions.AsString 
            sortText = json.TryGetProperty("sortText") |> Option.map JsonExtensions.AsString 
            filterText = json.TryGetProperty("filterText") |> Option.map JsonExtensions.AsString 
            insertText = json.TryGetProperty("insertText") |> Option.map JsonExtensions.AsString 
            insertTextFormat = json.TryGetProperty("insertTextFormat") |> Option.map JsonExtensions.AsInteger |> Option.map parseInsertTextFormat
            textEdit = json.TryGetProperty("textEdit") |> Option.map parseTextEdit
            additionalTextEdits = json.TryGetProperty("additionalTextEdits") |> Option.map JsonExtensions.AsArray |> noneAs [||] |> List.ofArray |> List.map parseTextEdit
            commitCharacters = json.TryGetProperty("commitCharacters") |> Option.map JsonExtensions.AsArray |> noneAs [||] |> List.ofArray |> List.map JsonExtensions.AsString |> List.map char
            command = json.TryGetProperty("command") |> Option.map parseCommand
            data = json.TryGetProperty("data") |> noneAs JsonValue.Null
        }

    let parseReferenceContext (json: JsonValue): ReferenceContext = 
        {
            includeDeclaration = json?includeDeclaration.AsBoolean()
        }

    let parseReferenceParams (json: JsonValue): ReferenceParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            position = json?position |> parsePosition
            context = json?context |> parseReferenceContext
        }

    let parseDocumentSymbolParams (json: JsonValue): DocumentSymbolParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
        }

    let parseWorkspaceSymbolParams (json: JsonValue): WorkspaceSymbolParams = 
        {
            query = json?query.AsString()
        }

    let parseDiagnosticSeverity (i: int): DiagnosticSeverity = 
        match i with 
        | 1 -> Error 
        | 2 -> Warning 
        | 3 -> Information 
        | 4 -> Hint

    let parseDiagnostic (json: JsonValue): Diagnostic = 
        {
            range = json?range |> parseRange
            severity = json.TryGetProperty("severity") |> Option.map JsonExtensions.AsInteger |> Option.map parseDiagnosticSeverity
            code = json.TryGetProperty("code") |> Option.map JsonExtensions.AsString 
            source = json.TryGetProperty("source") |> Option.map JsonExtensions.AsString
            message = json?message.AsString()
        }

    let parseCodeActionContext (json: JsonValue): CodeActionContext = 
        {
            diagnostics = json?diagnostics.AsArray() |> List.ofArray |> List.map parseDiagnostic
        }

    let parseCodeActionParams (json: JsonValue): CodeActionParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            range = json?range |> parseRange
            context = json?context |> parseCodeActionContext
        }

    let parseCodeLensParams (json: JsonValue): CodeLensParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
        }

    let parseCodeLens (json: JsonValue): CodeLens = 
        {
            range = json?range |> parseRange 
            command = json.TryGetProperty("command") |> Option.map parseCommand 
            data = json.TryGetProperty("data") |> noneAs JsonValue.Null
        }

    let parseDocumentLinkParams (json: JsonValue): DocumentLinkParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
        }

    let parseDocumentLink (json: JsonValue): DocumentLink = 
        {
            range = json?range |> parseRange
            target = json.TryGetProperty("target") |> Option.map JsonExtensions.AsString |> Option.map Uri 
        }

    let parseDocumentFormattingOptions (json: JsonValue): DocumentFormattingOptions = 
        {
            tabSize = json?tabSize.AsInteger()
            insertSpaces = json?insertSpaces.AsBoolean()
        }

    let valueAsString (key: string, value: JsonValue) = (key, value.AsString())

    let parseDocumentFormattingParams (json: JsonValue): DocumentFormattingParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            options = json?options |> parseDocumentFormattingOptions
            optionsMap = json?options.Properties |> Seq.map valueAsString |> Map.ofSeq
        }

    let parseDocumentRangeFormattingParams (json: JsonValue): DocumentRangeFormattingParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            options = json?options |> parseDocumentFormattingOptions
            optionsMap = json?options.Properties |> Seq.map valueAsString |> Map.ofSeq
            range = json?range |> parseRange
        }

    let parseDocumentOnTypeFormattingParams (json: JsonValue): DocumentOnTypeFormattingParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            options = json?options |> parseDocumentFormattingOptions
            optionsMap = json?options.Properties |> Seq.map valueAsString |> Map.ofSeq
            position = json?position |> parsePosition
            ch = json?ch.AsString() |> char
        }

    let parseRenameParams (json: JsonValue): RenameParams = 
        {
            textDocument = json?textDocument |> parseTextDocumentIdentifier
            position = json?position |> parsePosition
            newName = json?newName.AsString()
        }

    let parseExecuteCommandParams (json: JsonValue): ExecuteCommandParams = 
        {
            command = json?command.AsString()
            arguments = json.TryGetProperty("arguments") |> Option.map JsonExtensions.AsArray |> Option.map List.ofSeq |> noneAs []
        }

    let parseRequest (method: string) (json: JsonValue): Request = 
        match method with 
        | "initialize" -> Initialize (parseInitialize json)
        | "textDocument/willSaveWaitUntil" -> WillSaveWaitUntilTextDocument (parseWillSaveTextDocumentParams json)
        | "textDocument/completion" -> Completion (parseTextDocumentPositionParams json)
        | "textDocument/hover" -> Hover (parseTextDocumentPositionParams json)
        | "completionItem/resolve" -> ResolveCompletionItem (parseCompletionItem json)
        | "textDocument/signatureHelp" -> SignatureHelp (parseTextDocumentPositionParams json)
        | "textDocument/definition" -> GotoDefinition (parseTextDocumentPositionParams json)
        | "textDocument/references" -> FindReferences (parseReferenceParams json)
        | "textDocument/documentHighlight" -> DocumentHighlight (parseTextDocumentPositionParams json)
        | "textDocument/documentSymbol" -> DocumentSymbols (parseDocumentSymbolParams json)
        | "workspace/symbol" -> WorkspaceSymbols (parseWorkspaceSymbolParams json)
        | "textDocument/codeAction" -> CodeActions (parseCodeActionParams json)
        | "textDocument/codeLens" -> CodeLens (parseCodeLensParams json)
        | "codeLens/resolve" -> ResolveCodeLens (parseCodeLens json)
        | "textDocument/documentLink" -> DocumentLink (parseDocumentLinkParams json)
        | "documentLink/resolve" -> ResolveDocumentLink (parseDocumentLink json)
        | "textDocument/formatting" -> DocumentFormatting (parseDocumentFormattingParams json)
        | "textDocument/rangeFormatting" -> DocumentRangeFormatting (parseDocumentRangeFormattingParams json)
        | "textDocument/onTypeFormatting" -> DocumentOnTypeFormatting (parseDocumentOnTypeFormattingParams json)
        | "textDocument/rename" -> Rename (parseRenameParams json)
        | "workspace/executeCommand" -> ExecuteCommand (parseExecuteCommandParams json)
        | _ -> raise (Exception (sprintf "Unexpected request method %s" method))