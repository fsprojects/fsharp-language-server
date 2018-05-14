module LSP.ParserTests

open Types
open System
open Parser
open SimpleTest
open LSP.Json

let ``test parse a RequestMessage`` (t: TestContext) = 
    let json = """{
        "jsonrpc": "2.0",
        "id": 1,
        "method": "helloWorld",
        "params": {"hello": "world"}
    }"""
    let found = parseMessage json
    let expected = (RequestMessage (1, "helloWorld", JsonValue.Parse """{"hello":"world"}"""))
    if found <> expected then Fail(found)

let ``test parse a RequestMessage with params`` (t: TestContext) = 
    let json = """{
        "jsonrpc": "2.0",
        "id": 1,
        "method": "helloWorld",
        "params": {"hello": "world"}
    }"""
    let found = parseMessage json
    let expected = (RequestMessage (1, "helloWorld", JsonValue.Parse """{"hello":"world"}"""))
    if found <> expected then Fail(found)

let ``test parse a NotificationMessage`` (t: TestContext) = 
    let json = """{
        "jsonrpc": "2.0",
        "method": "helloNotification"
    }"""
    let found = parseMessage json
    let expected = (NotificationMessage ("helloNotification", None))
    if found <> expected then Fail(found)

let ``test parse a NotificationMessage with params`` (t: TestContext) = 
    let json = """{
        "jsonrpc": "2.0",
        "method": "helloNotification",
        "params": {"hello": "world"}
    }"""
    let found = parseMessage json
    let expected = NotificationMessage ("helloNotification", Some (JsonValue.Parse """{"hello":"world"}"""))
    if found <> expected then Fail(found)

let ``test parse an Initialized notification`` (t: TestContext) = 
    let found = parseNotification "initialized" None
    if found <> Initialized then Fail(found)

let ``test parse a DidChangeConfiguration notification`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "settings": {"hello": "world"}
    }"""
    let found = parseNotification "workspace/didChangeConfiguration" (Some json)
    let expected = (DidChangeConfiguration {
            settings = JsonValue.Parse """{"hello":"world"}"""
        })
    if found <> expected then Fail(found)

let ``test parse a DidOpenTextDocument notification`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs",
            "languageId": "fsharp",
            "version": 1,
            "text": "let x = 1"
        }
    }"""
    let found = parseNotification "textDocument/didOpen" (Some json)
    let expected = (DidOpenTextDocument {
            textDocument = 
                {
                    uri = Uri("file://workspace/Main.fs")
                    languageId = "fsharp"
                    version = 1
                    text = "let x = 1"
                }
        })
    if found <> expected then Fail(found)

let ``test parse a DidChangeTextDocument notification`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs",
            "version": 1
        },
        "contentChanges": [{
            "text": "let x = 1"
        }]
    }"""
    let found = parseNotification "textDocument/didChange" (Some json)
    let expected = (DidChangeTextDocument {
            textDocument = 
                {
                    uri = Uri("file://workspace/Main.fs")
                    version = 1
                }
            contentChanges = 
            [
                {
                    range = None 
                    rangeLength = None
                    text = "let x = 1"
                }
            ]
        })
    if found <> expected then Fail(found)

let ``test parse a DidChangeTextDocument notification with range`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs",
            "version": 1
        },
        "contentChanges": [{
            "range": {
                "start": {"line": 0, "character": 0},
                "end": {"line": 0, "character": 3}
            },
            "rangeLength": 3,
            "text": "let x = 1"
        }]
    }"""
    let found = parseNotification "textDocument/didChange" (Some json)
    let expected = (DidChangeTextDocument {
            textDocument = 
                {
                    uri = Uri("file://workspace/Main.fs")
                    version = 1
                }
            contentChanges = 
            [{
                range = Some {
                    start = {line = 0; character = 0}
                    ``end`` = {line = 0; character = 3}
                }
                rangeLength = Some 3
                text = "let x = 1"
            }]
        })
    if found <> expected then Fail(found)

let ``test parse a WillSaveTextDocument notification`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "reason": 2
    }"""
    let found = parseNotification "textDocument/willSave" (Some json)
    let expected = (WillSaveTextDocument {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            reason = TextDocumentSaveReason.AfterDelay
        })
    if found <> expected then Fail(found)

let ``test parse a WillSaveWaitUntilTextDocument request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "reason": 2
    }"""
    let found = parseRequest "textDocument/willSaveWaitUntil" json
    let expected = (WillSaveWaitUntilTextDocument {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            reason = TextDocumentSaveReason.AfterDelay
        })
    if found <> expected then Fail(found)

let ``test parse a DidSaveTextDocument notification`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        }
    }"""
    let found = parseNotification "textDocument/didSave" (Some json)
    let expected = (DidSaveTextDocument {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            text = None
        })
    if found <> expected then Fail(found)

let ``test parse a DidSaveTextDocument notification with text`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "text": "let x = 1"
    }"""
    let found = parseNotification "textDocument/didSave" (Some json)
    let expected = (DidSaveTextDocument {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            text = Some "let x = 1"
        })
    if found <> expected then Fail(found)

let ``test parse a DidCloseTextDocument notification`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        }
    }"""
    let found = parseNotification "textDocument/didClose" (Some json)
    let expected = (DidCloseTextDocument {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
        })
    if found <> expected then Fail(found)

let ``test parse a DidChangeWatchedFiles notification`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "changes": [{
            "uri": "file://workspace/Main.fs",
            "type": 2
        }]
    }"""
    let found = parseNotification "workspace/didChangeWatchedFiles" (Some json)
    let expected = (DidChangeWatchedFiles {
            changes = 
                [{
                    uri = Uri("file://workspace/Main.fs")
                    ``type`` = FileChangeType.Changed
                }]
        })
    if found <> expected then Fail(found)

let ``test parse a minimal Initialize request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "processId": 1,
        "rootUri": "file://workspace",
        "capabilities": {
        }
    }"""
    let (Initialize i) = parseRequest "initialize" json
    let expected = (
            {
                processId = Some 1;
                rootUri = Some (Uri("file://workspace"));
                initializationOptions = None;
                capabilitiesMap = Map.empty;
                trace = None
            }
        )
    if i <> expected then Fail(i)

let ``test processId can be null`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "processId": null,
        "rootUri": "file://workspace",
        "capabilities": {
        }
    }"""
    let (Initialize i) = parseRequest "initialize" json 
    if i.processId <> None then Fail(i.processId)
    
let ``test parse capabilities as map`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "processId": 1,
        "rootUri": "file://workspace",
        "capabilities": {
            "workspace": {
                "workspaceEdit": {
                    "documentChanges": true
                }
            }
        }
    }"""
    let (Initialize i) = parseRequest "initialize" json 
    let expected = Map.empty.Add("workspace.workspaceEdit.documentChanges", true)
    if i.capabilitiesMap <> expected then Fail(i.capabilitiesMap)

let ``test parse Completion request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "position": {
            "line": 0,
            "character": 5
        }
    }"""
    let found = parseRequest "textDocument/completion" json
    let expected = (Completion {
            textDocument = 
                {
                    uri = Uri("file://workspace/Main.fs")
                }
            position = 
                {
                    line = 0
                    character = 5
                }
        })
    if found <> expected then Fail(found)

let ``test parse minimal ResolveCompletionItem request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "label": "foo"
    }"""
    let found = parseRequest "completionItem/resolve" json
    let expected = (ResolveCompletionItem {
            label = "foo"
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
        })
    if found <> expected then Fail(found)

let ``test parse maximal ResolveCompletionItem request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "label": "foo",
        "kind": 1,
        "detail": "foo(): string",
        "documentation": "Foo returns foo",
        "sortText": "1/foo",
        "filterText": "foo",
        "insertText": "foo()",
        "insertTextFormat": 1,
        "textEdit": {
            "range": {
                "start": {"line": 0, "character": 0},
                "end": {"line": 0, "character": 2}
            },
            "newText": "foo()"
        },
        "additionalTextEdits": [{
            "range": {
                "start": {"line": 1, "character": 0},
                "end": {"line": 1, "character": 0}
            },
            "newText": "foo()"
        }],
        "commitCharacters": ["\t"],
        "command": {
            "title": "eval",
            "command": "do/eval",
            "arguments": [{"hello":"world"}]
        },
        "data": {"hello":"world"}
    }"""
    let found = parseRequest "completionItem/resolve" json
    let expected = (ResolveCompletionItem {
            label = "foo"
            kind = Some CompletionItemKind.Text
            detail = Some "foo(): string" 
            documentation = Some "Foo returns foo" 
            sortText = Some "1/foo" 
            filterText = Some "foo" 
            insertText = Some "foo()" 
            insertTextFormat = Some InsertTextFormat.PlainText 
            textEdit = Some {
                range = 
                    {
                        start = {line = 0; character = 0}
                        ``end`` = {line = 0; character = 2}
                    }
                newText = "foo()"
            } 
            additionalTextEdits = 
                [{
                    range = 
                        {
                            start = {line = 1; character = 0}
                            ``end`` = {line = 1; character = 0}
                        }
                    newText = "foo()"
                }]
            commitCharacters = ['\t']
            command = Some {
                title = "eval"
                command = "do/eval"
                arguments = [JsonValue.Parse """{"hello":"world"}"""]
            } 
            data = JsonValue.Parse """{"hello":"world"}"""
        })
    if found <> expected then Fail(found)

let ``test parse SignatureHelp request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "position": {
            "line": 0,
            "character": 5
        }
    }"""
    let found = parseRequest "textDocument/signatureHelp" json
    let expected = (SignatureHelp {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            position = 
                {
                    line = 0
                    character = 5
                }
        })
    if found <> expected then Fail(found)

let ``test parse GotoDefinition request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "position": {
            "line": 0,
            "character": 5
        }
    }"""
    let found = parseRequest "textDocument/definition" json
    let expected = (GotoDefinition {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            position = 
                {
                    line = 0
                    character = 5
                }
        })
    if found <> expected then Fail(found)

let ``test parse FindReferences request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "position": {
            "line": 0,
            "character": 5
        },
        "context": {
            "includeDeclaration": true
        }
    }"""
    let found = parseRequest "textDocument/references" json
    let expected = (FindReferences {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            position = { line = 0; character = 5 }
            context = { includeDeclaration = true }
        })
    if found <> expected then Fail(found)

let ``test parse DocumentHighlight request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "position": {
            "line": 0,
            "character": 5
        }
    }"""
    let found = parseRequest "textDocument/documentHighlight" json
    let expected = (DocumentHighlight {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            position = { line = 0; character = 5 }
        })
    if found <> expected then Fail(found)

let ``test parse DocumentSymbols request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        }
    }"""
    let found = parseRequest "textDocument/documentSymbol" json
    let expected = (DocumentSymbols {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
        })
    if found <> expected then Fail(found)

let ``test parse WorkspaceSymbols request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "query": "foo"
    }"""
    let found = parseRequest "workspace/symbol" json
    let expected = (WorkspaceSymbols {
            query = "foo"
        })
    if found <> expected then Fail(found)

let ``test parse minimal CodeActions request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "range": {
            "start": {"line": 1, "character": 0},
            "end": {"line": 1, "character": 0}
        },
        "context": {
            "diagnostics": [{
                "range": {
                    "start": {"line": 1, "character": 0},
                    "end": {"line": 1, "character": 0}
                },
                "message": "Some error"
            }]
        }
    }"""
    let found = parseRequest "textDocument/codeAction" json
    let expected = (CodeActions {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            range = 
                {
                    start = {line = 1; character = 0}
                    ``end`` = {line = 1; character = 0}
                }
            context = 
                {
                    diagnostics = 
                        [{
                            range = 
                                {
                                    start = {line = 1; character = 0}
                                    ``end`` = {line = 1; character = 0}
                                }
                            severity = None
                            code = None
                            source = None
                            message = "Some error"
                        }]
                }
        })
    if found <> expected then Fail(found)

let ``test parse maximal CodeActions request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "range": {
            "start": {"line": 1, "character": 0},
            "end": {"line": 1, "character": 0}
        },
        "context": {
            "diagnostics": [{
                "range": {
                    "start": {"line": 1, "character": 0},
                    "end": {"line": 1, "character": 0}
                },
                "severity": 1,
                "code": "SomeError",
                "source": "compiler",
                "message": "Some error"
            }]
        }
    }"""
    let found = parseRequest "textDocument/codeAction" json
    let expected = (CodeActions {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            range = 
                { 
                    start = {line = 1; character = 0}
                    ``end`` = {line = 1; character = 0} 
                }
            context = 
                {
                    diagnostics = 
                        [{
                            range = 
                                { 
                                    start = {line = 1; character = 0}
                                    ``end`` = {line = 1; character = 0} 
                                }
                            severity = Some DiagnosticSeverity.Error
                            code = Some "SomeError"
                            source = Some "compiler"
                            message = "Some error"
                        }]
                }
        })
    if found <> expected then Fail(found)

let ``test parse CodeActions request with an integer code`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "range": {
            "start": {"line": 1, "character": 0},
            "end": {"line": 1, "character": 0}
        },
        "context": {
            "diagnostics": [{
                "range": {
                    "start": {"line": 1, "character": 0},
                    "end": {"line": 1, "character": 0}
                },
                "code": 1,
                "message": "Some error"
            }]
        }
    }"""
    let found = parseRequest "textDocument/codeAction" json
    let expected = (CodeActions {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            range = 
                {
                    start = {line = 1; character = 0}
                    ``end`` = {line = 1; character = 0}
                }
            context = 
                {
                    diagnostics = 
                        [{
                            range = 
                                {
                                    start = {line = 1; character = 0}
                                    ``end`` = {line = 1; character = 0}
                                }
                            severity = None
                            code = Some "1"
                            source = None
                            message = "Some error"
                        }]
                }
        })
    if found <> expected then Fail(found)

let ``test parse CodeLens request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        }
    }"""
    let found = parseRequest "textDocument/codeLens" json
    let expected = (Request.CodeLens {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
        })
    if found <> expected then Fail(found)

let ``test parse minimal ResolveCodeLens request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "range": {
            "start": {"line": 1, "character": 0},
            "end": {"line": 1, "character": 0}
        }
    }"""
    let found = parseRequest "codeLens/resolve" json
    let expected = (ResolveCodeLens {
            range = 
                {
                    start = {line = 1; character = 0}
                    ``end`` = {line = 1; character = 0}
                }
            command = None 
            data = JsonValue.Null
        })
    if found <> expected then Fail(found)

let ``test parse maximal ResolveCodeLens request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "range": {
            "start": {"line": 1, "character": 0},
            "end": {"line": 1, "character": 0}
        },
        "command": {
            "title": "save",
            "command": "doSave",
            "arguments": ["hi"]
        },
        "data": "hi"
    }"""
    let found = parseRequest "codeLens/resolve" json
    let expected = (ResolveCodeLens {
            range = 
                {
                    start = {line = 1; character = 0}
                    ``end`` = {line = 1; character = 0}
                }
            command = Some {
                title = "save"
                command = "doSave"
                arguments = [JsonValue.String "hi"]
            } 
            data = JsonValue.String "hi"
        })
    if found <> expected then Fail(found)

let ``test parse DocumentLink request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        }
    }"""
    let found = parseRequest "textDocument/documentLink" json
    let expected = (DocumentLink {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
        })
    if found <> expected then Fail(found)

let ``test parse ResolveDocumentLink request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "range": {
            "start": {"line": 1, "character": 0},
            "end": {"line": 1, "character": 0}
        }
    }"""
    let found = parseRequest "documentLink/resolve" json
    let expected = (ResolveDocumentLink {
            range = 
                {
                    start = {line = 1; character = 0}
                    ``end`` = {line = 1; character = 0}
                }
            target = None
        })
    if found <> expected then Fail(found)

let ``test parse DocumentFormatting request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "options": {
            "tabSize": 1,
            "insertSpaces": true,
            "boolOption": true,
            "intOption": 1,
            "stringOption": "foo"
        }
    }"""
    let found = parseRequest "textDocument/formatting" json
    let expected = (DocumentFormatting {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            options = 
                {
                    tabSize = 1
                    insertSpaces = true 
                }
            optionsMap = Map.ofSeq 
                [
                    "tabSize", "1";
                    "insertSpaces", "true";
                    "boolOption", "true";
                    "intOption", "1";
                    "stringOption", "foo"
                ]
        })
    if found <> expected then Fail(found)

let ``test parse DocumentRangeFormatting request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "options": {
            "tabSize": 1,
            "insertSpaces": true,
            "boolOption": true,
            "intOption": 1,
            "stringOption": "foo"
        },
        "range": {
            "start": {"line": 1, "character": 0},
            "end": {"line": 1, "character": 0}
        }
    }"""
    let found = parseRequest "textDocument/rangeFormatting" json
    let expected = (DocumentRangeFormatting {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            options = 
                {
                    tabSize = 1
                    insertSpaces = true 
                }
            optionsMap = Map.ofSeq 
                [
                    "tabSize", "1";
                    "insertSpaces", "true";
                    "boolOption", "true";
                    "intOption", "1";
                    "stringOption", "foo"
                ]
            range = 
                {
                    start = {line = 1; character = 0}
                    ``end`` = {line = 1; character = 0}
                }
        })
    if found <> expected then Fail(found)

let ``test parse DocumentOnTypeFormatting request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "options": {
            "tabSize": 1,
            "insertSpaces": true,
            "boolOption": true,
            "intOption": 1,
            "stringOption": "foo"
        },
        "position": {
            "line": 0,
            "character": 0
        },
        "ch": "a"
    }"""
    let found = parseRequest "textDocument/onTypeFormatting" json
    let expected = (DocumentOnTypeFormatting {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            options = 
                {
                    tabSize = 1
                    insertSpaces = true 
                }
            optionsMap = Map.ofSeq 
                [
                    "tabSize", "1";
                    "insertSpaces", "true";
                    "boolOption", "true";
                    "intOption", "1";
                    "stringOption", "foo"
                ]
            position = 
                {
                    line = 0
                    character = 0
                }
            ch = 'a'
        })
    if found <> expected then Fail(found)

let ``test parse Rename request`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "textDocument": {
            "uri": "file://workspace/Main.fs"
        },
        "position": {
            "line": 0,
            "character": 0
        },
        "newName": "foo"
    }"""
    let found = parseRequest "textDocument/rename" json
    let expected = (Rename {
            textDocument = { uri = Uri("file://workspace/Main.fs") }
            position = 
                {
                    line = 0
                    character = 0
                }
            newName = "foo"
        })
    if found <> expected then Fail(found)

let ``test parse ExecuteCommand request with no arguments`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "command": "foo"
    }"""
    let found = parseRequest "workspace/executeCommand" json
    let expected = (ExecuteCommand {
            command = "foo"
            arguments = []
        })
    if found <> expected then Fail(found)

let ``test parse ExecuteCommand request with arguments`` (t: TestContext) = 
    let json = JsonValue.Parse """{
        "command": "foo",
        "arguments": ["bar"]
    }"""
    let found = parseRequest "workspace/executeCommand" json
    let expected = (ExecuteCommand {
            command = "foo"
            arguments = [JsonValue.String "bar"]
        })
    if found <> expected then Fail(found)
