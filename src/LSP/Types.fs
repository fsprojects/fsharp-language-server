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