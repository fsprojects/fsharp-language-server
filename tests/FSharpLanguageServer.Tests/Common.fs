module FSharpLanguageServer.Tests.Common

open System
open System.IO

let private findProjectRoot(start: DirectoryInfo): DirectoryInfo = 
    seq {
        let mutable dir = start 
        while dir <> dir.Root do 
            for _ in dir.GetFiles "fsharp-language-server.sln" do 
                yield dir
            dir <- dir.Parent
    } |> Seq.head
let private testDirectory = DirectoryInfo(Directory.GetCurrentDirectory())

/// The root of the project folder
let projectRoot = findProjectRoot(testDirectory)