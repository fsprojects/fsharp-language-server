module LSP.DocumentStoreTests

open System
open System.Text
open System.IO
open Types



LSP.Log.diagnosticsLog := stdout
open Expecto

[<Tests>]
let tests=
    testList "formatting" [

    test "convert prefix Range to offsets"{
        let text = "foo\r\n\
                    bar\r\n\
                    doh" 
        let textBuilder = new StringBuilder(text)
        let range = 
            {   start = {line = 0; character = 0}
                ``end`` = {line = 0; character = 3} }
        let found = DocumentStoreUtils.findRange(textBuilder, range)
        Expect.equal (0, 3) found "not equal"
    }

    test "convert suffix Range to offsets"{
        let text = "foo\r\n\
                    bar\r\n\
                    doh" 
        let textBuilder = new StringBuilder(text)
        let range = 
            { start = {line = 2; character = 1}
              ``end`` = {line = 2; character = 3} }
        let found = DocumentStoreUtils.findRange(textBuilder, range)
        Expect.equal (11, 13) found "not equal"
    }


    test "convert line-spanning Range to offsets"{
        let text = "foo\r\n\
                    bar\r\n\
                    doh" 
        let textBuilder = new StringBuilder(text)
        let range = 
            { start = {line = 1; character = 2}
              ``end`` = {line = 2; character = 1} }
        let found = DocumentStoreUtils.findRange(textBuilder, range)
        Expect.equal (7, 11) found "not equal"
    }
    let exampleUri = Uri("file://" + Directory.GetCurrentDirectory() + "example.txt")


    test "open document"{
        let store = DocumentStore() 
        let exampleUri = exampleUri
        let helloWorld = "Hello world!"
        let openDoc: DidOpenTextDocumentParams = 
            { textDocument = 
                { uri = exampleUri
                  languageId = "plaintext" 
                  version = 1
                  text = helloWorld } }
        store.Open(openDoc)
        let found = store.GetText(FileInfo(exampleUri.LocalPath))
        Expect.equal (Some(helloWorld) ) found "not equal"
    }
    let helloStore()= 
        let store = DocumentStore() 
        let helloWorld = "Hello world!"
        let openDoc: DidOpenTextDocumentParams = 
            { textDocument = 
                { uri = exampleUri
                  languageId = "plaintext" 
                  version = 1
                  text = helloWorld } }
        store.Open(openDoc)
        store
    

    test "replace a document"{
        let store = helloStore()
        let newText = "Replaced everything"
        let replaceAll: DidChangeTextDocumentParams = 
            {   textDocument = 
                    { uri = exampleUri
                      version = 2 }
                contentChanges = 
                    [ { range = None
                        rangeLength = None 
                        text = newText } ] }
        store.Change(replaceAll)
        let found = store.GetText(FileInfo(exampleUri.LocalPath))
        Expect.equal (Some(newText)) found "not equal"
    }

    test "patch a document"{
        let store = helloStore()
        let newText = "George"
        let replaceAll: DidChangeTextDocumentParams = 
            {   textDocument = 
                    { uri = exampleUri
                      version = 2 }
                contentChanges = 
                    [   { 
                        range = Some { 
                            start = {line = 0; character = 6} 
                            ``end`` = {line = 0; character = 11} }
                        rangeLength = None 
                        text = newText } ]
            }
        store.Change(replaceAll)
        let found = store.GetText(FileInfo(exampleUri.LocalPath))
        Expect.equal (Some("Hello George!")) found "not equal"
    }
    ]