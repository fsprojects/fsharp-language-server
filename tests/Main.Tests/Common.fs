module Main.Tests.Common

open System
open System.IO
open Xunit

let private findProjectRoot (start: DirectoryInfo): DirectoryInfo = 
    seq {
        let mutable dir = start 
        while dir <> dir.Root do 
            for _ in dir.GetFiles "fsharp-language-server.sln" do 
                yield dir
            dir <- dir.Parent
    } |> Seq.head
let private testDirectory = DirectoryInfo(Directory.GetCurrentDirectory())
let projectRoot = findProjectRoot testDirectory