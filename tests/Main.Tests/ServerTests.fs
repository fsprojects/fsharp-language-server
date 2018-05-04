module Main.Tests.ServerTests

open Main.Tests.Common
open Main.Program
open LSP.Types
open LSP
open System
open System.IO
open SimpleTest
open Microsoft.FSharp.Compiler.SourceCodeServices
open SimpleTest
open System.Reflection
open System.Diagnostics

let ``test check errors in some text`` (t: TestContext) = 
    let file = "MyScript.fsx"
    let input = """
    let foo () = "foo!""
    """
    let checker = FSharpChecker.Create()
    let projOptions, projOptionsErrors = checker.GetProjectOptionsFromScript(file, input) |> Async.RunSynchronously
    let parsingOptions, parsingOptionsErrors = checker.GetParsingOptionsFromProjectOptions(projOptions)
    let parseFileResults = checker.ParseFile(file, input, parsingOptions) |> Async.RunSynchronously
    ()

let ``test report a type error when a file is opened`` (t: TestContext) = 
    let server = Server() :> ILanguageServer
    let file = Path.Combine [|projectRoot.FullName; "sample"; "WrongType.fs"|]
    let fileText = File.ReadAllText(file)
    let openParams: DidOpenTextDocumentParams = {textDocument={uri=Uri(file); languageId="fsharp"; version=0; text=fileText}}
    server.DidOpenTextDocument openParams

let ``test that we can find tests`` (t: TestContext) = 
    Fail("Failed!")