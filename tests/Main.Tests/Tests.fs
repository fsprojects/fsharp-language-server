module Main.Tests

open LSP
open System
open NUnit.Framework
open Microsoft.FSharp.Compiler.SourceCodeServices

[<Test>]
let ``check errors in some text`` () = 
    let file = "MyScript.fsx"
    let input = """
    let foo () = "foo!""
    """
    let checker = FSharpChecker.Create()
    let projOptions, projOptionsErrors = checker.GetProjectOptionsFromScript(file, input) |> Async.RunSynchronously
    let parsingOptions, parsingOptionsErrors = checker.GetParsingOptionsFromProjectOptions(projOptions)
    let parseFileResults = checker.ParseFile(file, input, parsingOptions) |> Async.RunSynchronously
    for error in parseFileResults.Errors do 
        Log.info "%d:%d %s" error.StartLineAlternate error.StartColumn error.Message
    Log.info "Finished %s" parseFileResults.FileName