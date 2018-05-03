module LSP.DocumentStoreTests

open System
open System.Text
open Xunit
open Types

[<Fact>]
let ``convert prefix Range to offsets`` () = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 0; character = 0}
          _end = {line = 0; character = 3} }
    Assert.Equal(DocumentStoreUtils.findRange textBuilder range, (0, 3))

[<Fact>]
let ``convert suffix Range to offsets`` () = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 2; character = 1}
          _end = {line = 2; character = 3} }
    Assert.Equal(DocumentStoreUtils.findRange textBuilder range, (11, 13))

[<Fact>]
let ``convert line-spanning Range to offsets`` () = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 1; character = 2}
          _end = {line = 2; character = 1} }
    Assert.Equal(DocumentStoreUtils.findRange textBuilder range, (7, 11))

[<Fact>]
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
    Assert.Equal(store.GetText(exampleUri), (Some helloWorld))

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

[<Fact>]
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
    Assert.Equal(store.GetText(exampleUri), (Some newText))

[<Fact>]
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
    Assert.Equal(store.GetText(exampleUri), (Some "Hello George!"))