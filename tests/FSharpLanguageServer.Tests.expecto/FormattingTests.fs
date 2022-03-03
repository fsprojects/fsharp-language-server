module FSharpLanguageServer.Tests.FormattingTests

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharpLanguageServer.Tests.Common
open FSharpLanguageServer
open System
open System.IO
open LSP.Types 
open FSharp.Data
open FSharp.Compiler.CodeAnalysis
open ServerTests
open Expecto

[<Tests>]
let tests=
    testList "formatting" [

        test "hover over function With alias type" {
            let client, server = createServerAndReadFile("MainProject", "Hover.fs")
            match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 19, 6)) |> Async.RunSynchronously with 
            | None -> failtest("No hover")
            | Some hover ->Expect.isNonEmpty hover.contents "Hover list is empty"

        }

        test "hover_over_System_function" { 
            let client, server = createServerAndReadFile("MainProject", "Hover.fs")
            
            match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 14, 36)) |> Async.RunSynchronously with 
            | None -> failtest("No hover")
            | Some hover -> 
                if List.isEmpty hover.contents then failtest("Hover list is empty")
                let matches=hover.contents|>List.filter(fun x->
                    let doc=
                        match x with
                        |PlainString(s)->s
                        |HighlightedString(s,_)->s
                    Expect.isFalse (doc.Contains "<summary>") "Documentation contains xml tag <summary> meaning it was not correctly formatted with xml tags removed"
                    doc.Contains("Applies a function to each element of the collection, threading an accumulator argument")
                    )
                Expect.isNonEmpty matches "List does not contain required System function doc string"
                }

        test "hoverMethodParams"{  
            let client, server = createServerAndReadFile("MainProject", "Hover.fs")
            
            match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 25, 19)) |> Async.RunSynchronously with 
            | None -> failtest("No hover")
            | Some hover -> 
                if List.isEmpty hover.contents then failtest("Hover list is empty")
                let matches=hover.contents|>List.filter(fun x->
                    let doc=
                        match x with
                        |PlainString(s)->s
                        |HighlightedString(s,_)->s
                    doc.Contains("The encrypted authorization message expected by the server.")
                    )
                Expect.isNonEmpty matches "List does not contain required function doc string" 
        }

        test "hoverMDDocs"{  
            let client, server = createServerAndReadFile("MainProject", "Hover.fs")
            
            match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 23, 10)) |> Async.RunSynchronously with 
            | None -> failtest("No hover")
            | Some hover -> 
                if List.isEmpty hover.contents then failtest("Hover list is empty")
                let matches=hover.contents|>List.filter(fun x->
                    let doc=
                        match x with
                        |PlainString(s)->s
                        |HighlightedString(s,_)->s
                    doc.Contains("This function has documentation")
                    )
                Expect.isNonEmpty matches "List does not contain required function doc string from a non-xml comment"
        }
    ]
        

