// Learn more about F# at http://fsharp.org

open System
open Microsoft.FSharp.Compiler.SourceCodeServices

let file = "MyScript.fsx"
let input = """
let foo () = "foo!""
"""
let checker = FSharpChecker.Create()
let projOptions, projOptionsErrors = checker.GetProjectOptionsFromScript(file, input) |> Async.RunSynchronously
let parsingOptions, parsingOptionsErrors = checker.GetParsingOptionsFromProjectOptions(projOptions)
let parseFileResults = checker.ParseFile(file, input, parsingOptions) |> Async.RunSynchronously

[<EntryPoint>]
let main argv =
    for error in parseFileResults.Errors do 
        printfn "%d:%d %s" error.StartLineAlternate error.StartColumn error.Message
    printfn "Finished %s" parseFileResults.FileName
    0 // return an integer exit code
