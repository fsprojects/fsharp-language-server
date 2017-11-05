module LSP.DocumentStoreTests

open System
open NUnit.Framework
open Types

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