module LSP.ParserTests

open Types
open System
open Parser
open FSharp.Data

open Expecto

LSP.Log.diagnosticsLog := stdout

[<Tests>]
let tests  =        
    testList "Parser test" [
    test "parse a RequestMessage"{
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld",
            "params": {"hello": "world"}
        }"""
        let found = parseMessage json
        let expected = (RequestMessage (1, "helloWorld", JsonValue.Parse """{"hello":"world"}"""))
        Expect.equal expected found "not equal"
    }

    test "parse a RequestMessage with params"{
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld",
            "params": {"hello": "world"}
        }"""
        let found = parseMessage json
        let expected = (RequestMessage (1, "helloWorld", JsonValue.Parse """{"hello":"world"}"""))
        Expect.equal expected found "not equal"
    }

    test "parse a NotificationMessage"{
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification"
        }"""
        let found = parseMessage json
        let expected = (NotificationMessage ("helloNotification", None))
        Expect.equal expected found "not equal"
    }

    test "parse a NotificationMessage with params"{
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification",
            "params": {"hello": "world"}
        }"""
        let found = parseMessage json
        let expected = NotificationMessage ("helloNotification", Some (JsonValue.Parse """{"hello":"world"}"""))
        Expect.equal expected found "not equal"
    }

    test "parse an Initialized notification"{
        let found = parseNotification("initialized", None)
        Expect.equal Initialized found "not equal"
    }

    test "parse a DidChangeConfiguration notification"{
        let json = JsonValue.Parse """{
            "settings": {"hello": "world"}
        }"""
        let found = parseNotification("workspace/didChangeConfiguration", Some(json))
        let expected = (DidChangeConfiguration {
                settings = JsonValue.Parse """{"hello":"world"}"""
            })
        Expect.equal expected found "not equal"
    }

    test "parse a DidOpenTextDocument notification"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs",
                "languageId": "fsharp",
                "version": 1,
                "text": "let x = 1"
            }
        }"""
        let found = parseNotification("textDocument/didOpen", Some(json))
        let expected = (DidOpenTextDocument {
                textDocument = 
                    {
                        uri = Uri("file://workspace/FSharpLanguageServer.fs")
                        languageId = "fsharp"
                        version = 1
                        text = "let x = 1"
                    }
            })
        Expect.equal expected found "not equal"
    }

    test "parse a DidChangeTextDocument notification"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs",
                "version": 1
            },
            "contentChanges": [{
                "text": "let x = 1"
            }]
        }"""
        let found = parseNotification("textDocument/didChange", Some(json))
        let expected = (DidChangeTextDocument {
                textDocument = 
                    {
                        uri = Uri("file://workspace/FSharpLanguageServer.fs")
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
        Expect.equal expected found "not equal"
    }

    test "parse a DidChangeTextDocument notification with range"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs",
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
        let found = parseNotification("textDocument/didChange", Some(json))
        let expected = (DidChangeTextDocument {
                textDocument = 
                    {
                        uri = Uri("file://workspace/FSharpLanguageServer.fs")
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
        Expect.equal expected found "not equal"
    }

    test "parse a WillSaveTextDocument notification"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "reason": 2
        }"""
        let found = parseNotification("textDocument/willSave", Some(json))
        let expected = (WillSaveTextDocument {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                reason = TextDocumentSaveReason.AfterDelay
            })
        Expect.equal expected found "not equal"
    }

    test "parse a WillSaveWaitUntilTextDocument request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "reason": 2
        }"""
        let found = parseRequest("textDocument/willSaveWaitUntil", json)
        let expected = (WillSaveWaitUntilTextDocument {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                reason = TextDocumentSaveReason.AfterDelay
            })
        Expect.equal expected found "not equal"
    }

    test "parse a DidSaveTextDocument notification"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            }
        }"""
        let found = parseNotification("textDocument/didSave", Some(json))
        let expected = (DidSaveTextDocument {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                text = None
            })
        Expect.equal expected found "not equal"
    }

    test "parse a DidSaveTextDocument notification with text"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "text": "let x = 1"
        }"""
        let found = parseNotification("textDocument/didSave", Some(json))
        let expected = (DidSaveTextDocument {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                text = Some "let x = 1"
            })
        Expect.equal expected found "not equal"
    }

    test "parse a DidCloseTextDocument notification"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            }
        }"""
        let found = parseNotification("textDocument/didClose", Some(json))
        let expected = (DidCloseTextDocument {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
            })
        Expect.equal expected found "not equal"
    }

    test "parse a DidChangeWatchedFiles notification"{
        let json = JsonValue.Parse """{
            "changes": [{
                "uri": "file://workspace/FSharpLanguageServer.fs",
                "type": 2
            }]
        }"""
        let found = parseNotification("workspace/didChangeWatchedFiles", Some(json))
        let expected = (DidChangeWatchedFiles {
                changes = 
                    [{
                        uri = Uri("file://workspace/FSharpLanguageServer.fs")
                        ``type`` = FileChangeType.Changed
                    }]
            })
        Expect.equal expected found "not equal"
    }

    test "parse a minimal Initialize request"{
        let json = JsonValue.Parse """{
            "processId": 1,
            "rootUri": "file:///workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest("initialize", json)
        let expected = (
                {
                    processId = Some(1);
                    rootUri = Some(Uri("file:///workspace"));
                    initializationOptions = None;
                    capabilitiesMap = Map.empty;
                    trace = None
                }
            )
        Expect.equal expected i "not equal"
    }

    test "parse a hash character in a URI"{
        let json = JsonValue.Parse """{
            "processId": 1,
            "rootUri": "file:///dir/f%23-test",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest("initialize", json)
        Expect.equal "/dir/f#-test" i.rootUri.Value.LocalPath "not equal"
    }

    test "parse a colon character in a URI"{
        let json = JsonValue.Parse """{
            "processId": 1,
            "rootUri": "file:///c%3A/Projects/test-fs",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest("initialize", json)
        Expect.equal "c:\\Projects\\test-fs" i.rootUri.Value.LocalPath "not equal"
    }

    test "processId can be null"{
        let json = JsonValue.Parse """{
            "processId": null,
            "rootUri": "file://workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest("initialize", json) 
        Expect.equal None i.processId "not equal"
        
    }

    test "parse capabilities as map"{
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
        let (Initialize i) = parseRequest("initialize", json) 
        let expected = Map.empty.Add("workspace.workspaceEdit.documentChanges", true)
        Expect.equal expected i.capabilitiesMap "not equal"
    }

    test "parse Completion request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        let found = parseRequest("textDocument/completion", json)
        let expected = (Completion {
                textDocument = 
                    {
                        uri = Uri("file://workspace/FSharpLanguageServer.fs")
                    }
                position = 
                    {
                        line = 0
                        character = 5
                    }
            })
        Expect.equal expected found "not equal"
    }

    test "parse minimal ResolveCompletionItem request"{
        let json = JsonValue.Parse """{
            "label": "foo"
        }"""
        let found = parseRequest("completionItem/resolve", json)
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
        Expect.equal expected found "not equal"
    }

    test "parse maximal ResolveCompletionItem request"{
        let json = JsonValue.Parse """{
            "label": "foo",
            "kind": 1,
            "detail": "foo(): string",
            "documentation": {"kind":"markdown", "value":"Foo returns foo"},
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
        let found = parseRequest("completionItem/resolve", json)
        let expected = (ResolveCompletionItem {
                label = "foo"
                kind = Some CompletionItemKind.Text
                detail = Some "foo(): string" 
                documentation = Some({kind=MarkupKind.Markdown; value="Foo returns foo"})
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
        Expect.equal expected found "not equal"
    }

    test "parse SignatureHelp request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        let found = parseRequest("textDocument/signatureHelp", json)
        let expected = (SignatureHelp {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                position = 
                    {
                        line = 0
                        character = 5
                    }
            })
        Expect.equal expected found "not equal"
    }

    test "parse GotoDefinition request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        let found = parseRequest("textDocument/definition", json)
        let expected = (GotoDefinition {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                position = 
                    {
                        line = 0
                        character = 5
                    }
            })
        Expect.equal expected found "not equal"
    }

    test "parse FindReferences request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            },
            "context": {
                "includeDeclaration": true
            }
        }"""
        let found = parseRequest("textDocument/references", json)
        let expected = (FindReferences {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                position = { line = 0; character = 5 }
                context = { includeDeclaration = true }
            })
        Expect.equal expected found "not equal"
    }

    test "parse DocumentHighlight request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        let found = parseRequest("textDocument/documentHighlight", json)
        let expected = (DocumentHighlight {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                position = { line = 0; character = 5 }
            })
        Expect.equal expected found "not equal"
    }

    test "parse DocumentSymbols request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            }
        }"""
        let found = parseRequest("textDocument/documentSymbol", json)
        let expected = (DocumentSymbols {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
            })
        Expect.equal expected found "not equal"
    }

    test "parse WorkspaceSymbols request"{
        let json = JsonValue.Parse """{
            "query": "foo"
        }"""
        let found = parseRequest("workspace/symbol", json)
        let expected = (WorkspaceSymbols {
                query = "foo"
            })
        Expect.equal expected found "not equal"
    }

    test "parse minimal CodeActions request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
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
        let found = parseRequest("textDocument/codeAction", json)
        let expected = (CodeActions {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
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
        Expect.equal expected found "not equal"
    }

    test "parse maximal CodeActions request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
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
        let found = parseRequest("textDocument/codeAction", json)
        let expected = (CodeActions {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
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
        Expect.equal expected found "not equal"
    }

    test "parse CodeActions request with an integer code"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
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
        let found = parseRequest("textDocument/codeAction", json)
        let expected = (CodeActions {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
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
        Expect.equal expected found "not equal"
    }

    test "parse CodeLens request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            }
        }"""
        let found = parseRequest("textDocument/codeLens", json)
        let expected = (Request.CodeLens {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
            })
        Expect.equal expected found "not equal"
    }

    test "parse minimal ResolveCodeLens request"{
        let json = JsonValue.Parse """{
            "range": {
                "start": {"line": 1, "character": 0},
                "end": {"line": 1, "character": 0}
            }
        }"""
        let found = parseRequest("codeLens/resolve", json)
        let expected = (ResolveCodeLens {
                range = 
                    {
                        start = {line = 1; character = 0}
                        ``end`` = {line = 1; character = 0}
                    }
                command = None 
                data = JsonValue.Null
            })
        Expect.equal expected found "not equal"
    }

    test "parse maximal ResolveCodeLens request"{
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
        let found = parseRequest("codeLens/resolve", json)
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
        Expect.equal expected found "not equal"
    }

    test "parse DocumentLink request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            }
        }"""
        let found = parseRequest("textDocument/documentLink", json)
        let expected = (DocumentLink {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
            })
        Expect.equal expected found "not equal"
    }

    test "parse ResolveDocumentLink request"{
        let json = JsonValue.Parse """{
            "range": {
                "start": {"line": 1, "character": 0},
                "end": {"line": 1, "character": 0}
            }
        }"""
        let found = parseRequest("documentLink/resolve", json)
        let expected = (ResolveDocumentLink {
                range = 
                    {
                        start = {line = 1; character = 0}
                        ``end`` = {line = 1; character = 0}
                    }
                target = None
            })
        Expect.equal expected found "not equal"
    }

    test "parse DocumentFormatting request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "options": {
                "tabSize": 1,
                "insertSpaces": true,
                "boolOption": true,
                "intOption": 1,
                "stringOption": "foo"
            }
        }"""
        let found = parseRequest("textDocument/formatting", json)
        let expected = (DocumentFormatting {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
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
        Expect.equal expected found "not equal"
    }

    test "parse DocumentRangeFormatting request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
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
        let found = parseRequest("textDocument/rangeFormatting", json)
        let expected = (DocumentRangeFormatting {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
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
        Expect.equal expected found "not equal"
    }

    test "parse DocumentOnTypeFormatting request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
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
        let found = parseRequest("textDocument/onTypeFormatting", json)
        let expected = (DocumentOnTypeFormatting {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
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
        Expect.equal expected found "not equal"
    }

    test "parse Rename request"{
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/FSharpLanguageServer.fs"
            },
            "position": {
                "line": 0,
                "character": 0
            },
            "newName": "foo"
        }"""
        let found = parseRequest("textDocument/rename", json)
        let expected = (Rename {
                textDocument = { uri = Uri("file://workspace/FSharpLanguageServer.fs") }
                position = 
                    {
                        line = 0
                        character = 0
                    }
                newName = "foo"
            })
        Expect.equal expected found "not equal"
    }

    test "parse ExecuteCommand request with no arguments"{
        let json = JsonValue.Parse """{
            "command": "foo"
        }"""
        let found = parseRequest("workspace/executeCommand", json)
        let expected = (ExecuteCommand {
                command = "foo"
                arguments = []
            })
        Expect.equal expected found "not equal"
    }

    test "parse ExecuteCommand request with arguments"{
        let json = JsonValue.Parse """{
            "command": "foo",
            "arguments": ["bar"]
        }"""
        let found = parseRequest("workspace/executeCommand", json)
        let expected = (ExecuteCommand {
                command = "foo"
                arguments = [JsonValue.String "bar"]
            })
        Expect.equal expected found "not equal"
    }
]