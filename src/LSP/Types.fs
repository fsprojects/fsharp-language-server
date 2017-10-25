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
| DidSaveTextDocument of DidSaveTextDocumentParams
| DidCloseTextDocument of DidCloseTextDocumentParams
| DidChangeWatchedFiles of DidChangeWatchedFilesParams

type Location = {
    uri: Uri 
    range: Range 
}

type DiagnosticSeverity = 
| Error 
| Warning 
| Information 
| Hint

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

type InsertTextFormat = 
| PlainText 
| Snippet 

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

type CompletionItem = {
    label: string 
    kind: option<CompletionItemKind>
    detail: option<string>
    documentation: option<string>
    sortText: option<string>
    filterText: option<string>
    insertText: option<string>
    insertTextFormat: option<InsertTextFormat>
    textEdit: option<TextEdit>
    additionalTextEdits: list<TextEdit>
    commitCharacters: list<char>
    command: option<Command>
    data: JsonValue
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
    diagnostics: list<Diagnostic>
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
    command: option<Command>
    data: JsonValue
}

type DocumentLinkParams = {
    textDocument: TextDocumentIdentifier
}

type DocumentLink = {
    range: Range 
    target: option<Uri>
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
    arguments: list<JsonValue>
}

[<RequireQualifiedAccess>]
type TextDocumentSyncKind = 
| None 
| Full
| Incremental

type CompletionOptions = {
    resolveProvider: bool 
    triggerCharacters: list<char>
}

let defaultCompletionOptions = {
    resolveProvider = false 
    triggerCharacters = ['.']
}

type SignatureHelpOptions = {
    triggerCharacters: list<char>
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
    moreTriggerCharacter: list<char>
}

type DocumentLinkOptions = {
    resolveProvider: bool
}

let defaultDocumentLinkOptions = {
    resolveProvider = false
}

type ExecuteCommandOptions = {
    commands: list<string>
}

type SaveOptions = {
    includeText: bool
}

let defaultSaveOptions = {
    includeText = false
}

type TextDocumentSyncOptions = {
    openClose: bool
    change: TextDocumentSyncKind
    willSave: bool
    willSaveWaitUntil: bool
    save: SaveOptions
}

let defaultTextDocumentSyncOptions = {
    openClose = false
    change = TextDocumentSyncKind.Incremental
    willSave = false 
    willSaveWaitUntil = false
    save = defaultSaveOptions
}

type ServerCapabilities = {
    textDocumentSync: TextDocumentSyncOptions
    hoverProvider: bool
    completionProvider: option<CompletionOptions>
    signatureHelpProvider: option<SignatureHelpOptions>
    definitionProvider: bool
    referencesProvider: bool
    documentHighlightProvider: bool
    documentSymbolProvider: bool
    workspaceSymbolProvider: bool
    codeActionProvider: bool
    codeLensProvider: option<CodeLensOptions>
    documentFormattingProvider: bool
    documentRangeFormattingProvider: bool
    documentOnTypeFormattingProvider: option<DocumentOnTypeFormattingOptions>
    renameProvider: bool
    documentLinkProvider: option<DocumentLinkOptions>
    executeCommandProvider: option<ExecuteCommandOptions>
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
    items: list<CompletionItem>
}

type MarkedString = 
| HighlightedString of value: string * language: string 
| PlainString of string

type Hover = {
    contents: list<MarkedString>
    range: option<Range>
}

type ParameterInformation = {
    label: string 
    documentation: option<string>
}

type SignatureInformation = {
    label: string 
    documentation: option<string>
    parameters: list<ParameterInformation>
}

type SignatureHelp = {
    signatures: list<SignatureInformation>
    activeSignature: option<int>
    activeParameter: option<int>
}

[<RequireQualifiedAccess>]
type DocumentHighlightKind = 
| Text 
| Read 
| Write 

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

type SymbolInformation = {
    name: string 
    kind: SymbolKind 
    location: Location
    containerName: option<string>
}

type ILanguageServer = 
    abstract member Initialize: InitializeParams -> InitializeResult
    abstract member Initialized: unit -> unit 
    abstract member Shutdown: unit -> Unit 
    abstract member Exit: unit -> unit 
    abstract member DidChangeConfiguration: DidChangeConfigurationParams -> unit 
    abstract member DidOpenTextDocument: DidOpenTextDocumentParams -> unit 
    abstract member DidChangeTextDocument: DidChangeTextDocumentParams -> unit 
    abstract member WillSaveTextDocument: WillSaveTextDocumentParams -> unit
    abstract member WillSaveWaitUntilTextDocument: WillSaveTextDocumentParams -> list<TextEdit>
    abstract member DidSaveTextDocument: DidSaveTextDocumentParams -> unit
    abstract member DidCloseTextDocument: DidCloseTextDocumentParams -> unit
    abstract member DidChangeWatchedFiles: DidChangeWatchedFilesParams -> unit
    abstract member Completion: TextDocumentPositionParams -> CompletionList
    abstract member Hover: TextDocumentPositionParams -> Hover
    abstract member ResolveCompletionItem: CompletionItem -> CompletionItem
    abstract member SignatureHelp: TextDocumentPositionParams -> SignatureHelp
    abstract member GotoDefinition: TextDocumentPositionParams -> list<Location>
    abstract member FindReferences: ReferenceParams -> list<Location>
    abstract member DocumentHighlight: TextDocumentPositionParams -> list<DocumentHighlight>
    abstract member DocumentSymbols: DocumentSymbolParams -> list<SymbolInformation>
    abstract member WorkspaceSymbols: WorkspaceSymbolParams -> list<SymbolInformation>
    abstract member CodeActions: CodeActionParams -> list<Command>
    abstract member CodeLens: CodeLensParams -> List<CodeLens>
    abstract member ResolveCodeLens: CodeLens -> CodeLens
    abstract member DocumentLink: DocumentLinkParams -> list<DocumentLink>
    abstract member ResolveDocumentLink: DocumentLink -> DocumentLink -> DocumentLink
    abstract member DocumentFormatting: DocumentFormattingParams -> list<TextEdit>
    abstract member DocumentRangeFormatting: DocumentRangeFormattingParams -> list<TextEdit>
    abstract member DocumentOnTypeFormatting: DocumentOnTypeFormattingParams -> list<TextEdit>
    abstract member Rename: RenameParams -> WorkspaceEdit
    abstract member ExecuteCommand: ExecuteCommandParams -> unit

// TODO IAsyncLanguageServer that supports request cancellation