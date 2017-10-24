namespace LSP

open System
open Parser
open NUnit.Framework
open FSharp.Data

module ParserTests =
    [<Test>]
    let ``parse a RequestMessage`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld"
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (RequestMessage (1, "helloWorld", None)))

    [<Test>]
    let ``parse a RequestMessage with params`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld",
            "params": {"hello": "world"}
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (RequestMessage (1, "helloWorld", Some (JsonValue.Parse """{"hello":"world"}"""))))

    [<Test>]
    let ``parse a NotificationMessage`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification"
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (NotificationMessage ("helloNotification", None)))

    [<Test>]
    let ``parse a NotificationMessage with params`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification",
            "params": {"hello": "world"}
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (NotificationMessage ("helloNotification", Some (JsonValue.Parse """{"hello":"world"}"""))))

    [<Test>]
    let ``parse a Cancel notification`` () = 
        let json = JsonValue.Parse """{
            "id": 1
        }""" 
        Assert.That(
            parseNotification "cancel" (Some json), 
            Is.EqualTo (Cancel 1))

    [<Test>]
    let ``parse an Initialized notification`` () = 
        Assert.That(
            parseNotification "initialized" None,
            Is.EqualTo Initialized)

    
    [<Test>]
    let ``parse a DidChangeConfiguration notification`` () = 
        let json = JsonValue.Parse """{
            "settings": {"hello": "world"}
        }"""
        Assert.That(
            parseNotification "workspace/didChangeConfiguration" (Some json),
            Is.EqualTo (DidChangeConfiguration {
                settings = JsonValue.Parse """{"hello":"world"}"""
            }))

    [<Test>]
    let ``parse a DidOpenTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs",
                "languageId": "fsharp",
                "version": 1,
                "text": "let x = 1"
            }
        }"""
        Assert.That(
            parseNotification "textDocument/didOpen" (Some json),
            Is.EqualTo (DidOpenTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                    languageId = "fsharp"
                    version = 1
                    text = "let x = 1"
                }
            }))

    [<Test>]
    let ``parse a DidChangeTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs",
                "version": 1
            },
            "contentChanges": [{
                "text": "let x = 1"
            }]
        }"""
        Assert.That(
            parseNotification "textDocument/didChange" (Some json),
            Is.EqualTo (DidChangeTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                    version = 1
                }
                contentChanges = [{
                    range = None 
                    rangeLength = None
                    text = "let x = 1"
                }]
            }))

    [<Test>]
    let ``parse a DidChangeTextDocument notification with range`` () = 
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
        Assert.That(
            parseNotification "textDocument/didChange" (Some json),
            Is.EqualTo (DidChangeTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                    version = 1
                }
                contentChanges = [{
                    range = Some {
                        start = {line = 0; character = 0}
                        _end = {line = 0; character = 3}
                    }
                    rangeLength = Some 3
                    text = "let x = 1"
                }]
            }))

    [<Test>]
    let ``parse a WillSaveTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "reason": 2
        }"""
        Assert.That(
            parseNotification "textDocument/willSave" (Some json),
            Is.EqualTo (WillSaveTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                reason = AfterDelay
            }))

    [<Test>]
    let ``parse a WillSaveWaitUntilTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "reason": 2
        }"""
        Assert.That(
            parseNotification "textDocument/willSaveWaitUntil" (Some json),
            Is.EqualTo (WillSaveWaitUntilTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                reason = AfterDelay
            }))

    [<Test>]
    let ``parse a DidSaveTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.That(
            parseNotification "textDocument/didSave" (Some json),
            Is.EqualTo (DidSaveTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                text = None
            }))

    [<Test>]
    let ``parse a DidSaveTextDocument notification with text`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "text": "let x = 1"
        }"""
        Assert.That(
            parseNotification "textDocument/didSave" (Some json),
            Is.EqualTo (DidSaveTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                text = Some "let x = 1"
            }))

    [<Test>]
    let ``parse a DidCloseTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.That(
            parseNotification "textDocument/didClose" (Some json),
            Is.EqualTo (DidCloseTextDocument {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
            }))

    [<Test>]
    let ``parse a DidChangeWatchedFiles notification`` () = 
        let json = JsonValue.Parse """{
            "changes": [{
                "uri": "file://workspace/Main.fs",
                "type": 2
            }]
        }"""
        Assert.That(
            parseNotification "workspace/didChangeWatchedFiles" (Some json),
            Is.EqualTo (DidChangeWatchedFiles {
                changes = [{
                    uri = Uri("file://workspace/Main.fs")
                    _type = Changed
                }]
            }))

    [<Test>]
    let ``parse a minimal Initialize request`` () = 
        let json = JsonValue.Parse """{
            "processId": 1,
            "rootUri": "file://workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest "initialize" json
        Assert.That(
            i, 
            Is.EqualTo({
                processId = Some 1;
                rootUri = Some (Uri("file://workspace"));
                initializationOptions = None;
                capabilitiesMap = Map.empty;
                trace = None
            }))
    
    [<Test>]
    let ``processId can be null`` () = 
        let json = JsonValue.Parse """{
            "processId": null,
            "rootUri": "file://workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest "initialize" json 
        Assert.That(i.processId, Is.EqualTo(None))

    [<Test>]
    let ``parse capabilities as map`` () = 
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
        Assert.That(
            i.capabilitiesMap, 
            Is.EquivalentTo(Map.empty.Add("workspace.workspaceEdit.documentChanges", true)))

    [<Test>]
    let ``parse Completion request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        Assert.That(
            parseRequest "textDocument/completion" json,
            Is.EqualTo(Completion {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                position = {
                    line = 0
                    character = 5
                }
            }))

    [<Test>]
    let ``parse minimal ResolveCompletionItem request`` () = 
        let json = JsonValue.Parse """{
            "label": "foo"
        }"""
        Assert.That(
            parseRequest "completionItem/resolve" json,
            Is.EqualTo(ResolveCompletionItem {
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
            }))

    [<Test>]
    let ``parse maximal ResolveCompletionItem request`` () = 
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
        Assert.That(
            parseRequest "completionItem/resolve" json,
            Is.EqualTo(ResolveCompletionItem {
                label = "foo"
                kind = Some CompletionItemKind.Text
                detail = Some "foo(): string" 
                documentation = Some "Foo returns foo" 
                sortText = Some "1/foo" 
                filterText = Some "foo" 
                insertText = Some "foo()" 
                insertTextFormat = Some InsertTextFormat.PlainText 
                textEdit = Some {
                    range = {
                        start = {line = 0; character = 0}
                        _end = {line = 0; character = 2}
                    }
                    newText = "foo()"
                } 
                additionalTextEdits = [{
                    range = {
                        start = {line = 1; character = 0}
                        _end = {line = 1; character = 0}
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
            }))

    [<Test>]
    let ``parse SignatureHelp request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        Assert.That(
            parseRequest "textDocument/signatureHelp" json,
            Is.EqualTo(SignatureHelp {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                position = {
                    line = 0
                    character = 5
                }
            }))

    [<Test>]
    let ``parse GotoDefinition request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        Assert.That(
            parseRequest "textDocument/definition" json,
            Is.EqualTo(GotoDefinition {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                position = {
                    line = 0
                    character = 5
                }
            }))

    [<Test>]
    let ``parse FindReferences request`` () = 
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
        Assert.That(
            parseRequest "textDocument/references" json,
            Is.EqualTo(FindReferences {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                position = {
                    line = 0
                    character = 5
                }
                context = {
                    includeDeclaration = true
                }
            }))

    [<Test>]
    let ``parse DocumentHighlight request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "position": {
                "line": 0,
                "character": 5
            }
        }"""
        Assert.That(
            parseRequest "textDocument/documentHighlight" json,
            Is.EqualTo(DocumentHighlight {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                position = {
                    line = 0
                    character = 5
                }
            }))

    [<Test>]
    let ``parse DocumentSymbols request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.That(
            parseRequest "textDocument/documentSymbol" json,
            Is.EqualTo(DocumentSymbols {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
            }))

    [<Test>]
    let ``parse WorkspaceSymbols request`` () = 
        let json = JsonValue.Parse """{
            "query": "foo"
        }"""
        Assert.That(
            parseRequest "workspace/symbol" json,
            Is.EqualTo(WorkspaceSymbols {
                query = "foo"
            }))

    [<Test>]
    let ``parse minimal CodeActions request`` () = 
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
        Assert.That(
            parseRequest "textDocument/codeAction" json,
            Is.EqualTo(CodeActions {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                range = {
                    start = {line = 1; character = 0}
                    _end = {line = 1; character = 0}
                }
                context = {
                    diagnostics = [{
                        range = {
                            start = {line = 1; character = 0}
                            _end = {line = 1; character = 0}
                        }
                        severity = None
                        code = None
                        source = None
                        message = "Some error"
                    }]
                }
            }))

    [<Test>]
    let ``parse maximal CodeActions request`` () = 
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
        Assert.That(
            parseRequest "textDocument/codeAction" json,
            Is.EqualTo(CodeActions {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                range = {
                    start = {line = 1; character = 0}
                    _end = {line = 1; character = 0}
                }
                context = {
                    diagnostics = [{
                        range = {
                            start = {line = 1; character = 0}
                            _end = {line = 1; character = 0}
                        }
                        severity = Some DiagnosticSeverity.Error
                        code = Some "SomeError"
                        source = Some "compiler"
                        message = "Some error"
                    }]
                }
            }))

    [<Test>]
    let ``parse CodeActions request with an integer code`` () = 
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
        Assert.That(
            parseRequest "textDocument/codeAction" json,
            Is.EqualTo(CodeActions {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
                range = {
                    start = {line = 1; character = 0}
                    _end = {line = 1; character = 0}
                }
                context = {
                    diagnostics = [{
                        range = {
                            start = {line = 1; character = 0}
                            _end = {line = 1; character = 0}
                        }
                        severity = None
                        code = Some "1"
                        source = None
                        message = "Some error"
                    }]
                }
            }))

    [<Test>]
    let ``parse CodeLens request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.That(
            parseRequest "textDocument/codeLens" json,
            Is.EqualTo(Request.CodeLens {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
            }))

    [<Test>]
    let ``parse minimal ResolveCodeLens request`` () = 
        let json = JsonValue.Parse """{
            "range": {
                "start": {"line": 1, "character": 0},
                "end": {"line": 1, "character": 0}
            }
        }"""
        Assert.That(
            parseRequest "codeLens/resolve" json,
            Is.EqualTo(ResolveCodeLens {
                range = {
                    start = {line = 1; character = 0}
                    _end = {line = 1; character = 0}
                }
                command = None 
                data = JsonValue.Null
            }))

    [<Test>]
    let ``parse maximal ResolveCodeLens request`` () = 
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
        Assert.That(
            parseRequest "codeLens/resolve" json,
            Is.EqualTo(ResolveCodeLens {
                range = {
                    start = {line = 1; character = 0}
                    _end = {line = 1; character = 0}
                }
                command = Some {
                    title = "save"
                    command = "doSave"
                    arguments = [JsonValue.String "hi"]
                } 
                data = JsonValue.String "hi"
            }))

    [<Test>]
    let ``parse DocumentLink request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.That(
            parseRequest "textDocument/documentLink" json,
            Is.EqualTo(DocumentLink {
                textDocument = {
                    uri = Uri("file://workspace/Main.fs")
                }
            }))
    
    [<Test>]
    let ``parse ResolveDocumentLink request`` () = 
        let json = JsonValue.Parse """{
            "range": {
                "start": {"line": 1, "character": 0},
                "end": {"line": 1, "character": 0}
            }
        }"""
        Assert.That(
            parseRequest "documentLink/resolve" json,
            Is.EqualTo(ResolveDocumentLink {
                range = {
                    start = {line = 1; character = 0}
                    _end = {line = 1; character = 0}
                }
                target = None
            }))
