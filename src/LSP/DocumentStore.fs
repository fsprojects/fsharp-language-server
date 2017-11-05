namespace LSP 

open System
open System.Collections.Generic
open System.Text
open Types 

type private Version = {
    text: StringBuilder 
    version: int
}

type DocumentStore() = 
    let compareUris = 
        { new IEqualityComparer<Uri> with 
            member this.Equals(x, y) = 
                StringComparer.CurrentCulture.Equals(x, y)
            member this.GetHashCode(x) = 
                StringComparer.CurrentCulture.GetHashCode(x) }
    let cache = new Dictionary<Uri, Version>(compareUris)

    member this.Open(doc: DidOpenTextDocumentParams): unit = 
        let text = StringBuilder(doc.textDocument.text)
        let version = {text = text; version = doc.textDocument.version}
        cache.[doc.textDocument.uri] <- version

    member this.Change(doc: DidChangeTextDocumentParams): unit = 
        ()

    member this.GetText(uri: Uri): option<string> = 
        let found, value = cache.TryGetValue(uri)
        if found then Some (value.text.ToString()) else None 