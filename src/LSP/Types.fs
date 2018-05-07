module LSP.Types 

open System
open FSharp.Data

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
    ``end``: Position
}

type TextDocumentContentChangeEvent = {
    range: Range option
    rangeLength: int option
    text: string
}

type DidChangeTextDocumentParams = {
    textDocument: VersionedTextDocumentIdentifier
    contentChanges: TextDocumentContentChangeEvent list
}

type TextDocumentIdentifier = {
    uri: Uri
}

[<RequireQualifiedAccess>]
type TextDocumentSaveReason = 
| Manual
| AfterDelay
| FocusOut

let writeTextDocumentSaveReason i =
    match i with 
    | TextDocumentSaveReason.Manual -> 1
    | TextDocumentSaveReason.AfterDelay -> 2
    | TextDocumentSaveReason.FocusOut -> 3

type WillSaveTextDocumentParams = {
    textDocument: TextDocumentIdentifier
    reason: TextDocumentSaveReason
}

type DidSaveTextDocumentParams = {
    textDocument: TextDocumentIdentifier
    text: string option
}

type DidCloseTextDocumentParams = {
    textDocument: TextDocumentIdentifier
}

[<RequireQualifiedAccess>]
type FileChangeType = 
| Created
| Changed 
| Deleted

let writeFileChangeType i = 
    match i with 
    | FileChangeType.Created -> 1
    | FileChangeType.Changed -> 2
    | FileChangeType.Deleted -> 3

type FileEvent = {
    uri: Uri 
    ``type``: FileChangeType
}

type DidChangeWatchedFilesParams = {
    changes: FileEvent list
}
    
type Notification = 
| Cancel of id: int 
| Initialized
| Shutdown 
| DidChangeConfiguration of DidChangeConfigurationParams
| DidOpenTextDocument of DidOpenTextDocumentParams
| DidChangeTextDocument of DidChangeTextDocumentParams
| WillSaveTextDocument of WillSaveTextDocumentParams
| DidSaveTextDocument of DidSaveTextDocumentParams
| DidCloseTextDocument of DidCloseTextDocumentParams
| DidChangeWatchedFiles of DidChangeWatchedFilesParams
| OtherNotification of method: string

type Location = {
    uri: Uri 
    range: Range 
}

[<RequireQualifiedAccess>]
type DiagnosticSeverity = 
| Error 
| Warning 
| Information 
| Hint

let writeDiagnosticSeverity i = 
    match i with 
    | DiagnosticSeverity.Error -> 1 
    | DiagnosticSeverity.Warning -> 2
    | DiagnosticSeverity.Information -> 3
    | DiagnosticSeverity.Hint -> 4

type Diagnostic = {
    range: Range
    severity: DiagnosticSeverity option
    code: string option
    source: string option
    message: string;
}

type Command = {
    title: string
    command: string 
    arguments: JsonValue list
}

type TextEdit = {
    range: Range 
    newText: string
}

type TextDocumentEdit = {
    textDocument: VersionedTextDocumentIdentifier
    edits: TextEdit list
}

type WorkspaceEdit = {
    documentChanges: TextDocumentEdit list
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

type DocumentSelector = DocumentFilter list

[<RequireQualifiedAccess>]
type Trace = 
| Off 
| Messages 
| Verbose

let writeTrace i = 
    match i with 
    | Trace.Off -> "off"
    | Trace.Messages -> "messages"
    | Trace.Verbose -> "verbose"

type InitializeParams = {
    processId: int option
    rootUri: Uri option
    initializationOptions: JsonValue option
    capabilitiesMap: Map<string, bool>
    trace: Trace option
}

let defaultInitializeParams: InitializeParams = {
    processId = None 
    rootUri = None 
    initializationOptions = None 
    capabilitiesMap = Map.empty 
    trace = None
}

[<RequireQualifiedAccess>]
type InsertTextFormat = 
| PlainText 
| Snippet 

let writeInsertTextFormat (i: InsertTextFormat) = 
    match i with 
    | InsertTextFormat.PlainText -> 1
    | InsertTextFormat.Snippet -> 2

[<RequireQualifiedAccess>]
type CompletionItemKind = 
| Text
| Method
| Function
| Constructor
| Field
| Variable
| Class
| Interface
| Module
| Property
| Unit
| Value
| Enum
| Keyword
| Snippet
| Color
| File
| Reference

let writeCompletionItemKind (i: CompletionItemKind) = 
    match i with 
    | CompletionItemKind.Text -> 1
    | CompletionItemKind.Method -> 2
    | CompletionItemKind.Function -> 3
    | CompletionItemKind.Constructor -> 4
    | CompletionItemKind.Field -> 5
    | CompletionItemKind.Variable -> 6
    | CompletionItemKind.Class -> 7
    | CompletionItemKind.Interface -> 8
    | CompletionItemKind.Module -> 9
    | CompletionItemKind.Property -> 10
    | CompletionItemKind.Unit -> 11
    | CompletionItemKind.Value -> 12
    | CompletionItemKind.Enum -> 13
    | CompletionItemKind.Keyword -> 14
    | CompletionItemKind.Snippet -> 15
    | CompletionItemKind.Color -> 16
    | CompletionItemKind.File -> 17
    | CompletionItemKind.Reference -> 18

type CompletionItem = {
    label: string 
    kind: CompletionItemKind option
    detail: string option
    documentation: string option
    sortText: string option
    filterText: string option
    insertText: string option
    insertTextFormat: InsertTextFormat option
    textEdit: TextEdit option
    additionalTextEdits: TextEdit list
    commitCharacters: char list
    command: Command option
    data: JsonValue
}

let defaultCompletionItem: CompletionItem = {
    label = ""
    kind = None 
    detail = None 
    documentation = None 
    sortText = None 
    filterText = None
    insertText = None
    insertTextFormat = None
    textEdit = None
    additionalTextEdits = []
    commitCharacters = []
    command = None
    data = JsonValue.Null
}

type ReferenceContext = {
    includeDeclaration: bool
}

type ReferenceParams = {
    textDocument: TextDocumentIdentifier
    position: Position
    context: ReferenceContext
}

type DocumentSymbolParams = {
    textDocument: TextDocumentIdentifier
}

type WorkspaceSymbolParams = {
    query: string
}

type CodeActionContext = {
    diagnostics: Diagnostic list
}

type CodeActionParams = {
    textDocument: TextDocumentIdentifier
    range: Range
    context: CodeActionContext
}

type CodeLensParams = {
    textDocument: TextDocumentIdentifier
}

type CodeLens = {
    range: Range 
    command: Command option
    data: JsonValue
}

type DocumentLinkParams = {
    textDocument: TextDocumentIdentifier
}

type DocumentLink = {
    range: Range 
    target: Uri option
}

type DocumentFormattingOptions = {
    tabSize: int 
    insertSpaces: bool 
}

type DocumentFormattingParams = {
    textDocument: TextDocumentIdentifier
    options: DocumentFormattingOptions
    optionsMap: Map<string, string>
}

type DocumentRangeFormattingParams = {
    textDocument: TextDocumentIdentifier
    options: DocumentFormattingOptions
    optionsMap: Map<string, string>
    range: Range
}

type DocumentOnTypeFormattingParams = {
    textDocument: TextDocumentIdentifier
    options: DocumentFormattingOptions
    optionsMap: Map<string, string>
    position: Position
    ch: char 
}

type RenameParams = {
    textDocument: TextDocumentIdentifier
    position: Position
    newName: string
}

type ExecuteCommandParams = {
    command: string 
    arguments: JsonValue list
}

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

[<RequireQualifiedAccess>]
type TextDocumentSyncKind = 
| None 
| Full
| Incremental

let writeTextDocumentSyncKind (i: TextDocumentSyncKind) = 
    match i with 
    | TextDocumentSyncKind.None -> 0
    | TextDocumentSyncKind.Full -> 1
    | TextDocumentSyncKind.Incremental -> 2

type CompletionOptions = {
    resolveProvider: bool 
    triggerCharacters: char list
}

let defaultCompletionOptions = {
    resolveProvider = false 
    triggerCharacters = ['.']
}

type SignatureHelpOptions = {
    triggerCharacters: char list
}

let defaultSignatureHelpOptions = {
    triggerCharacters = ['('; ',']
}

type CodeLensOptions = {
    resolveProvider: bool  
}

let defaultCodeLensOptions = {
    resolveProvider = false
}

type DocumentOnTypeFormattingOptions = {
    firstTriggerCharacter: char
    moreTriggerCharacter: char list
}

type DocumentLinkOptions = {
    resolveProvider: bool
}

let defaultDocumentLinkOptions = {
    resolveProvider = false
}

type ExecuteCommandOptions = {
    commands: string list
}

type SaveOptions = {
    includeText: bool
}

type TextDocumentSyncOptions = {
    openClose: bool
    change: TextDocumentSyncKind
    willSave: bool
    willSaveWaitUntil: bool
    save: SaveOptions option
}

let defaultTextDocumentSyncOptions = {
    openClose = false
    change = TextDocumentSyncKind.None
    willSave = false 
    willSaveWaitUntil = false
    save = None
}

type ServerCapabilities = {
    textDocumentSync: TextDocumentSyncOptions
    hoverProvider: bool
    completionProvider: CompletionOptions option
    signatureHelpProvider: SignatureHelpOptions option
    definitionProvider: bool
    referencesProvider: bool
    documentHighlightProvider: bool
    documentSymbolProvider: bool
    workspaceSymbolProvider: bool
    codeActionProvider: bool
    codeLensProvider: CodeLensOptions option
    documentFormattingProvider: bool
    documentRangeFormattingProvider: bool
    documentOnTypeFormattingProvider: DocumentOnTypeFormattingOptions option
    renameProvider: bool
    documentLinkProvider: DocumentLinkOptions option
    executeCommandProvider: ExecuteCommandOptions option
}

let defaultServerCapabilities: ServerCapabilities = {
    textDocumentSync = defaultTextDocumentSyncOptions
    hoverProvider = false
    completionProvider = None
    signatureHelpProvider = None
    definitionProvider = false
    referencesProvider = false
    documentHighlightProvider = false
    documentSymbolProvider = false
    workspaceSymbolProvider = false
    codeActionProvider = false
    codeLensProvider = None
    documentFormattingProvider = false
    documentRangeFormattingProvider = false
    documentOnTypeFormattingProvider = None
    renameProvider = false
    documentLinkProvider = None
    executeCommandProvider = None
}

type InitializeResult = {
    capabilities: ServerCapabilities
}

type CompletionList = {
    isIncomplete: bool 
    items: CompletionItem list
}

type MarkedString = 
| HighlightedString of value: string * language: string 
| PlainString of string

let writeMarkedString (s: MarkedString): JsonValue = 
    match s with 
    | HighlightedString (value, language) -> 
        JsonValue.Record 
            [| "language", (JsonValue.String language);
               "value", (JsonValue.String value) |]
    | PlainString value -> 
        JsonValue.String value

type Hover = {
    contents: MarkedString list
    range: Range option
}

type ParameterInformation = {
    label: string 
    documentation: string option
}

type SignatureInformation = {
    label: string 
    documentation: string option
    parameters: ParameterInformation list
}

type SignatureHelp = {
    signatures: SignatureInformation list
    activeSignature: int option
    activeParameter: int option
}

[<RequireQualifiedAccess>]
type DocumentHighlightKind = 
| Text 
| Read 
| Write 

let writeDocumentHighlightKind (i: DocumentHighlightKind) = 
    match i with 
    | DocumentHighlightKind.Text -> 1
    | DocumentHighlightKind.Read -> 2
    | DocumentHighlightKind.Write -> 3 


type DocumentHighlight = {
    range: Range 
    kind: DocumentHighlightKind
}

[<RequireQualifiedAccess>]
type SymbolKind = 
| File
| Module
| Namespace
| Package
| Class
| Method
| Property
| Field
| Constructor
| Enum
| Interface
| Function
| Variable
| Constant
| String
| Number
| Boolean
| Array

let writeSymbolKind (i: SymbolKind) = 
    match i with
    | SymbolKind.File -> 1
    | SymbolKind.Module -> 2
    | SymbolKind.Namespace -> 3
    | SymbolKind.Package -> 4
    | SymbolKind.Class -> 5
    | SymbolKind.Method -> 6
    | SymbolKind.Property -> 7
    | SymbolKind.Field -> 8
    | SymbolKind.Constructor -> 9
    | SymbolKind.Enum -> 10
    | SymbolKind.Interface -> 11
    | SymbolKind.Function -> 12
    | SymbolKind.Variable -> 13
    | SymbolKind.Constant -> 14
    | SymbolKind.String -> 15
    | SymbolKind.Number -> 16
    | SymbolKind.Boolean -> 17
    | SymbolKind.Array -> 18

type SymbolInformation = {
    name: string 
    kind: SymbolKind 
    location: Location
    containerName: string option
}

type ILanguageServer = 
    abstract member Initialize: InitializeParams -> InitializeResult
    abstract member Initialized: unit -> unit 
    abstract member Shutdown: unit -> Unit 
    abstract member DidChangeConfiguration: DidChangeConfigurationParams -> unit 
    abstract member DidOpenTextDocument: DidOpenTextDocumentParams -> unit 
    abstract member DidChangeTextDocument: DidChangeTextDocumentParams -> unit 
    abstract member WillSaveTextDocument: WillSaveTextDocumentParams -> unit
    abstract member WillSaveWaitUntilTextDocument: WillSaveTextDocumentParams -> TextEdit list
    abstract member DidSaveTextDocument: DidSaveTextDocumentParams -> unit
    abstract member DidCloseTextDocument: DidCloseTextDocumentParams -> unit
    abstract member DidChangeWatchedFiles: DidChangeWatchedFilesParams -> unit
    abstract member Completion: TextDocumentPositionParams -> CompletionList option
    abstract member Hover: TextDocumentPositionParams -> Hover option
    abstract member ResolveCompletionItem: CompletionItem -> CompletionItem
    abstract member SignatureHelp: TextDocumentPositionParams -> SignatureHelp option
    abstract member GotoDefinition: TextDocumentPositionParams -> Location list
    abstract member FindReferences: ReferenceParams -> Location list
    abstract member DocumentHighlight: TextDocumentPositionParams -> DocumentHighlight list
    abstract member DocumentSymbols: DocumentSymbolParams -> SymbolInformation list
    abstract member WorkspaceSymbols: WorkspaceSymbolParams -> SymbolInformation list
    abstract member CodeActions: CodeActionParams -> Command list
    abstract member CodeLens: CodeLensParams -> CodeLens list
    abstract member ResolveCodeLens: CodeLens -> CodeLens
    abstract member DocumentLink: DocumentLinkParams -> DocumentLink list
    abstract member ResolveDocumentLink: DocumentLink -> DocumentLink
    abstract member DocumentFormatting: DocumentFormattingParams -> TextEdit list
    abstract member DocumentRangeFormatting: DocumentRangeFormattingParams -> TextEdit list
    abstract member DocumentOnTypeFormatting: DocumentOnTypeFormattingParams -> TextEdit list
    abstract member Rename: RenameParams -> WorkspaceEdit
    abstract member ExecuteCommand: ExecuteCommandParams -> unit

// TODO IAsyncLanguageServer that supports request cancellation

type PublishDiagnosticsParams = {
    uri: Uri 
    diagnostics: Diagnostic list
}

type ILanguageClient =
    abstract member PublishDiagnostics: PublishDiagnosticsParams -> unit 