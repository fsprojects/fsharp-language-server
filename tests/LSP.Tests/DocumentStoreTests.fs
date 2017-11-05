module LSP.DocumentStoreTests

open System
open System.Text
open NUnit.Framework
open Types

[<Test>]
let ``convert prefix Range to offsets`` () = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 0; character = 0}
          _end = {line = 0; character = 3} }
    Assert.That(DocumentStoreUtils.findRange textBuilder range, Is.EqualTo (0, 3))

[<Test>]
let ``convert suffix Range to offsets`` () = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 2; character = 1}
          _end = {line = 2; character = 3} }
    Assert.That(DocumentStoreUtils.findRange textBuilder range, Is.EqualTo (11, 13))

[<Test>]
let ``convert line-spanning Range to offsets`` () = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 1; character = 2}
          _end = {line = 2; character = 1} }
    Assert.That(DocumentStoreUtils.findRange textBuilder range, Is.EqualTo (7, 11))

[<Test>]
let ``open document`` () = 
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
    Assert.That(store.GetText(exampleUri), Is.EqualTo(Some helloWorld))

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
let ``replace a document`` () = 
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
    Assert.That(store.GetText(exampleUri), Is.EqualTo(Some newText))

[<Test>]
let ``patch a document`` () = 
    let store = helloStore()
    let newText = "George"
    let replaceAll: DidChangeTextDocumentParams = 
        { textDocument = 
            { uri = exampleUri
              version = 2 }
          contentChanges = 
            [ { range = Some { start = {line = 0; character = 6} 
                               _end = {line = 0; character = 11} }
                rangeLength = None 
                text = newText } ] }
    store.Change(replaceAll)
    Assert.That(store.GetText(exampleUri), Is.EqualTo(Some "Hello George!"))