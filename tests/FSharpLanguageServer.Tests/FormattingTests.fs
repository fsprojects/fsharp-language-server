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
