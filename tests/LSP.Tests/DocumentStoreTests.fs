module LSP.DocumentStoreTests

open System
open System.Text
open Types
open NUnit.Framework

[<SetUp>]
let setup() = 
    LSP.Log.diagnosticsLog := stdout

[<Test>]
let ``convert prefix Range to offsets``() = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 0; character = 0}
          ``end`` = {line = 0; character = 3} }
    let found = DocumentStoreUtils.findRange(textBuilder, range)
    Assert.AreEqual((0, 3), found)

[<Test>]
let ``convert suffix Range to offsets``() = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 2; character = 1}
          ``end`` = {line = 2; character = 3} }
    let found = DocumentStoreUtils.findRange(textBuilder, range)
    Assert.AreEqual((11, 13), found)

[<Test>]
let ``convert line-spanning Range to offsets``() = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 1; character = 2}
          ``end`` = {line = 2; character = 1} }
    let found = DocumentStoreUtils.findRange(textBuilder, range)
    Assert.AreEqual((7, 11), found)

[<Test>]
let ``open document``() = 
    let store = DocumentStore() 
    let exampleUri = Uri("file://example.txt")
    let helloWorld = "Hello world!"
    let openDoc: DidOpenTextDocumentParams = 
        { textDocument = 
            { uri = exampleUri
              languageId = "plaintext" 
              version = 1
              text = helloWorld } }
    store.Open(openDoc)
    let found = store.GetText(exampleUri)
    Assert.AreEqual(Some(helloWorld), found)

let exampleUri = Uri("file://example.txt")
let helloStore() = 
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

[<Test>]
let ``replace a document``() = 
    let store = helloStore()
    let newText = "Replaced everything"
    let replaceAll: DidChangeTextDocumentParams = 
        { textDocument = 
            { uri = exampleUri
              version = 2 }
          contentChanges = 
            [ { range = None
                rangeLength = None 
                text = newText } ] }
    store.Change(replaceAll)
    let found = store.GetText(exampleUri)
    Assert.AreEqual(Some(newText), found)

[<Test>]
let ``patch a document``() = 
    let store = helloStore()
    let newText = "George"
    let replaceAll: DidChangeTextDocumentParams = 
        { textDocument = 
            { uri = exampleUri
              version = 2 }
          contentChanges = 
            [ { range = Some { start = {line = 0; character = 6} 
                               ``end`` = {line = 0; character = 11} }
                rangeLength = None 
                text = newText } ] }
    store.Change(replaceAll)
    let found = store.GetText(exampleUri)
    Assert.AreEqual(Some("Hello George!"), found)