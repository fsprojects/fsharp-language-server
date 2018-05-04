module LSP.DocumentStoreTests

open System
open System.Text
open SimpleTest
open Types

let ``test convert prefix Range to offsets`` (t: TestContext) = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 0; character = 0}
          _end = {line = 0; character = 3} }
    let found = DocumentStoreUtils.findRange textBuilder range
    if found <> (0, 3) then Fail(found)

let ``test convert suffix Range to offsets`` (t: TestContext) = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 2; character = 1}
          _end = {line = 2; character = 3} }
    let found = DocumentStoreUtils.findRange textBuilder range
    if found <> (11, 13) then Fail(found)

let ``test convert line-spanning Range to offsets`` (t: TestContext) = 
    let text = "foo\r\n\
                bar\r\n\
                doh" 
    let textBuilder = new StringBuilder(text)
    let range = 
        { start = {line = 1; character = 2}
          _end = {line = 2; character = 1} }
    let found = DocumentStoreUtils.findRange textBuilder range
    if found <> (7, 11) then Fail(found)

let ``test open document`` (t: TestContext) = 
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
    if found <> (Some helloWorld) then Fail(found)

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

let ``test replace a document`` (t: TestContext) = 
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
    if found <> (Some newText) then Fail(found)

let ``test patch a document`` (t: TestContext) = 
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
    let found = store.GetText(exampleUri)
    if found <> (Some "Hello George!") then Fail(found)