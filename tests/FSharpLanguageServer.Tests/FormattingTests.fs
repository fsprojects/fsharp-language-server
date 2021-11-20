module FSharpLanguageServer.Tests.FormattingTests

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharpLanguageServer.Tests.Common
open FSharpLanguageServer
open System
open System.IO
open NUnit.Framework
open LSP.Types 
open FSharp.Data
open FSharp.Compiler.CodeAnalysis
open ServerTests
[<Test>]
let ``hover over function With alias type``() = 
    let client, server = createServerAndReadFile("MainProject", "Hover.fs")
    match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 19, 6)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")
[<Test>]
let hover_over_System_function() = 
    let client, server = createServerAndReadFile("MainProject", "Hover.fs")
    
    match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 14, 36)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> 
        if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")
        let matches=hover.contents|>List.filter(fun x->
            let doc=
                match x with
                |PlainString(s)->s
                |HighlightedString(s,_)->s
            Assert.False(doc.Contains "<summary>","Documentation contains xml tag <summary> meaning it was not correctly formatted with xml tags removed")
            doc.Contains("Applies a function to each element of the collection, threading an accumulator argument")
            )
        Assert.True(matches.Length>0,"List does not contain required System function doc string")
