namespace LSP

open Types
open System
open Parser
open Xunit
open FSharp.Data

module ParserTests =
    [<Fact>]
    let ``parse a RequestMessage`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld",
            "params": {"hello": "world"}
        }"""
        Assert.Equal(
            parseMessage json, 
            (RequestMessage (1, "helloWorld", JsonValue.Parse """{"hello":"world"}""")))

    [<Fact>]
    let ``parse a RequestMessage with params`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld",
            "params": {"hello": "world"}
        }"""
        Assert.Equal(
            parseMessage json, 
            (RequestMessage (1, "helloWorld", JsonValue.Parse """{"hello":"world"}""")))

    [<Fact>]
    let ``parse a NotificationMessage`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification"
        }"""
        Assert.Equal(
            parseMessage json, 
            (NotificationMessage ("helloNotification", None)))

    [<Fact>]
    let ``parse a NotificationMessage with params`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification",
            "params": {"hello": "world"}
        }"""
        Assert.Equal(
            parseMessage json, 
            (NotificationMessage ("helloNotification", Some (JsonValue.Parse """{"hello":"world"}"""))))

    [<Fact>]
    let ``parse a Cancel notification`` () = 
        let json = JsonValue.Parse """{
            "id": 1
        }""" 
        Assert.Equal(
            parseNotification "cancel" (Some json), 
            (Cancel 1))

    [<Fact>]
    let ``parse an Initialized notification`` () = 
        Assert.Equal(
            parseNotification "initialized" None,
            Initialized)

    
    [<Fact>]
    let ``parse a DidChangeConfiguration notification`` () = 
        let json = JsonValue.Parse """{
            "settings": {"hello": "world"}
        }"""
        Assert.Equal(
            parseNotification "workspace/didChangeConfiguration" (Some json),
            (DidChangeConfiguration {
                settings = JsonValue.Parse """{"hello":"world"}"""
            }))

    [<Fact>]
    let ``parse a DidOpenTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs",
                "languageId": "fsharp",
                "version": 1,
                "text": "let x = 1"
            }
        }"""
        Assert.Equal(
            parseNotification "textDocument/didOpen" (Some json),
            (DidOpenTextDocument {
                textDocument = 
                    {
                        uri = Uri("file://workspace/Main.fs")
                        languageId = "fsharp"
                        version = 1
                        text = "let x = 1"
                    }
            }))

    [<Fact>]
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
        Assert.Equal(
            parseNotification "textDocument/didChange" (Some json),
            (DidChangeTextDocument {
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
            }))

    [<Fact>]
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
        Assert.Equal(
            parseNotification "textDocument/didChange" (Some json),
            (DidChangeTextDocument {
                textDocument = 
                    {
                        uri = Uri("file://workspace/Main.fs")
                        version = 1
                    }
                contentChanges = 
                [{
                    range = Some {
                        start = {line = 0; character = 0}
                        _end = {line = 0; character = 3}
                    }
                    rangeLength = Some 3
                    text = "let x = 1"
                }]
            }))

    [<Fact>]
    let ``parse a WillSaveTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "reason": 2
        }"""
        Assert.Equal(
            parseNotification "textDocument/willSave" (Some json),
            (WillSaveTextDocument {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                reason = TextDocumentSaveReason.AfterDelay
            }))

    [<Fact>]
    let ``parse a WillSaveWaitUntilTextDocument request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "reason": 2
        }"""
        Assert.Equal(
            parseRequest "textDocument/willSaveWaitUntil" json,
            (WillSaveWaitUntilTextDocument {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                reason = TextDocumentSaveReason.AfterDelay
            }))

    [<Fact>]
    let ``parse a DidSaveTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.Equal(
            parseNotification "textDocument/didSave" (Some json),
            (DidSaveTextDocument {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                text = None
            }))

    [<Fact>]
    let ``parse a DidSaveTextDocument notification with text`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            },
            "text": "let x = 1"
        }"""
        Assert.Equal(
            parseNotification "textDocument/didSave" (Some json),
            (DidSaveTextDocument {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                text = Some "let x = 1"
            }))

    [<Fact>]
    let ``parse a DidCloseTextDocument notification`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.Equal(
            parseNotification "textDocument/didClose" (Some json),
            (DidCloseTextDocument {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
            }))

    [<Fact>]
    let ``parse a DidChangeWatchedFiles notification`` () = 
        let json = JsonValue.Parse """{
            "changes": [{
                "uri": "file://workspace/Main.fs",
                "type": 2
            }]
        }"""
        Assert.Equal(
            parseNotification "workspace/didChangeWatchedFiles" (Some json),
            (DidChangeWatchedFiles {
                changes = 
                    [{
                        uri = Uri("file://workspace/Main.fs")
                        _type = FileChangeType.Changed
                    }]
            }))

    [<Fact>]
    let ``parse a minimal Initialize request`` () = 
        let json = JsonValue.Parse """{
            "processId": 1,
            "rootUri": "file://workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest "initialize" json
        Assert.Equal(
            i, 
            (
                {
                    processId = Some 1;
                    rootUri = Some (Uri("file://workspace"));
                    initializationOptions = None;
                    capabilitiesMap = Map.empty;
                    trace = None
                }
            ))
    
    [<Fact>]
    let ``processId can be null`` () = 
        let json = JsonValue.Parse """{
            "processId": null,
            "rootUri": "file://workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest "initialize" json 
        Assert.Equal(i.processId, (None))

    [<Fact>]
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
        Assert.True(
            i.capabilitiesMap = (Map.empty.Add("workspace.workspaceEdit.documentChanges", true)))

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/completion" json,
            (Completion {
                textDocument = 
                    {
                        uri = Uri("file://workspace/Main.fs")
                    }
                position = 
                    {
                        line = 0
                        character = 5
                    }
            }))

    [<Fact>]
    let ``parse minimal ResolveCompletionItem request`` () = 
        let json = JsonValue.Parse """{
            "label": "foo"
        }"""
        Assert.Equal(
            parseRequest "completionItem/resolve" json,
            (ResolveCompletionItem {
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

    [<Fact>]
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
        Assert.Equal(
            parseRequest "completionItem/resolve" json,
            (ResolveCompletionItem {
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
                            _end = {line = 0; character = 2}
                        }
                    newText = "foo()"
                } 
                additionalTextEdits = 
                    [{
                        range = 
                            {
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

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/signatureHelp" json,
            (SignatureHelp {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                position = 
                    {
                        line = 0
                        character = 5
                    }
            }))

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/definition" json,
            (GotoDefinition {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                position = 
                    {
                        line = 0
                        character = 5
                    }
            }))

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/references" json,
            (FindReferences {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                position = { line = 0; character = 5 }
                context = { includeDeclaration = true }
            }))

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/documentHighlight" json,
            (DocumentHighlight {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                position = { line = 0; character = 5 }
            }))

    [<Fact>]
    let ``parse DocumentSymbols request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.Equal(
            parseRequest "textDocument/documentSymbol" json,
            (DocumentSymbols {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
            }))

    [<Fact>]
    let ``parse WorkspaceSymbols request`` () = 
        let json = JsonValue.Parse """{
            "query": "foo"
        }"""
        Assert.Equal(
            parseRequest "workspace/symbol" json,
            (WorkspaceSymbols {
                query = "foo"
            }))

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/codeAction" json,
            (CodeActions {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                range = 
                    {
                        start = {line = 1; character = 0}
                        _end = {line = 1; character = 0}
                    }
                context = 
                    {
                        diagnostics = 
                            [{
                                range = 
                                    {
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

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/codeAction" json,
            (CodeActions {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                range = 
                    { 
                        start = {line = 1; character = 0}
                        _end = {line = 1; character = 0} 
                    }
                context = 
                    {
                        diagnostics = 
                            [{
                                range = 
                                    { 
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

    [<Fact>]
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
        Assert.Equal(
            parseRequest "textDocument/codeAction" json,
            (CodeActions {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                range = 
                    {
                        start = {line = 1; character = 0}
                        _end = {line = 1; character = 0}
                    }
                context = 
                    {
                        diagnostics = 
                            [{
                                range = 
                                    {
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

    [<Fact>]
    let ``parse CodeLens request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.Equal(
            parseRequest "textDocument/codeLens" json,
            (Request.CodeLens {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
            }))

    [<Fact>]
    let ``parse minimal ResolveCodeLens request`` () = 
        let json = JsonValue.Parse """{
            "range": {
                "start": {"line": 1, "character": 0},
                "end": {"line": 1, "character": 0}
            }
        }"""
        Assert.Equal(
            parseRequest "codeLens/resolve" json,
            (ResolveCodeLens {
                range = 
                    {
                        start = {line = 1; character = 0}
                        _end = {line = 1; character = 0}
                    }
                command = None 
                data = JsonValue.Null
            }))

    [<Fact>]
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
        Assert.Equal(
            parseRequest "codeLens/resolve" json,
            (ResolveCodeLens {
                range = 
                    {
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

    [<Fact>]
    let ``parse DocumentLink request`` () = 
        let json = JsonValue.Parse """{
            "textDocument": {
                "uri": "file://workspace/Main.fs"
            }
        }"""
        Assert.Equal(
            parseRequest "textDocument/documentLink" json,
            (DocumentLink {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
            }))
    
    [<Fact>]
    let ``parse ResolveDocumentLink request`` () = 
        let json = JsonValue.Parse """{
            "range": {
                "start": {"line": 1, "character": 0},
                "end": {"line": 1, "character": 0}
            }
        }"""
        Assert.Equal(
            parseRequest "documentLink/resolve" json,
            (ResolveDocumentLink {
                range = 
                    {
                        start = {line = 1; character = 0}
                        _end = {line = 1; character = 0}
                    }
                target = None
            }))
    
    [<Fact>]
    let ``parse DocumentFormatting request`` () = 
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
        Assert.Equal(
            parseRequest "textDocument/formatting" json,
            (DocumentFormatting {
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
            }))
    
    [<Fact>]
    let ``parse DocumentRangeFormatting request`` () = 
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
        Assert.Equal(
            parseRequest "textDocument/rangeFormatting" json,
            (DocumentRangeFormatting {
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
                        _end = {line = 1; character = 0}
                    }
            }))
    
    [<Fact>]
    let ``parse DocumentOnTypeFormatting request`` () = 
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
        Assert.Equal(
            parseRequest "textDocument/onTypeFormatting" json,
            (DocumentOnTypeFormatting {
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
            }))
    
    [<Fact>]
    let ``parse Rename request`` () = 
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
        Assert.Equal(
            parseRequest "textDocument/rename" json,
            (Rename {
                textDocument = { uri = Uri("file://workspace/Main.fs") }
                position = 
                    {
                        line = 0
                        character = 0
                    }
                newName = "foo"
            }))
    
    [<Fact>]
    let ``parse ExecuteCommand request with no arguments`` () = 
        let json = JsonValue.Parse """{
            "command": "foo"
        }"""
        Assert.Equal(
            parseRequest "workspace/executeCommand" json,
            (ExecuteCommand {
                command = "foo"
                arguments = []
            }))
    
    [<Fact>]
    let ``parse ExecuteCommand request with arguments`` () = 
        let json = JsonValue.Parse """{
            "command": "foo",
            "arguments": ["bar"]
        }"""
        Assert.Equal(
            parseRequest "workspace/executeCommand" json,
            (ExecuteCommand {
                command = "foo"
                arguments = [JsonValue.String "bar"]
            }))
