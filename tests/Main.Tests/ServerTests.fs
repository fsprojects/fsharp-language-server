module Main.Tests.ServerTests

open Main.Tests.Common
open Main.Program
open LSP.Types
open LSP
open SimpleTest
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open System.Reflection
open System.Diagnostics
open System.Collections.Generic

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

type MockClient() = 
    member val Diagnostics = List<PublishDiagnosticsParams>()
    interface ILanguageClient with 
        member this.PublishDiagnostics (p: PublishDiagnosticsParams): unit = 
            this.Diagnostics.Add(p)

let createServer (): MockClient * ILanguageServer = 
    let client = MockClient()
    let server = Server(client) :> ILanguageServer
    (client, server)

let readFile (name: string): string * string = 
    let file = Path.Combine [|projectRoot.FullName; "sample"; name|]
    let fileText = File.ReadAllText(file)
    (file, fileText)

let createServerAndReadFile (name: string): MockClient * ILanguageServer = 
    let (client, server) = createServer()
    let (file, fileText) = readFile name
    let openParams: DidOpenTextDocumentParams = {textDocument={uri=Uri(file); languageId="fsharp"; version=0; text=fileText}}
    server.DidOpenTextDocument(openParams)
    (client, server)

let ``test report a type error when a file is opened`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "WrongType.fs"
    if client.Diagnostics.Count = 0 then Fail("No diagnostics")
    let messages = List.collect (fun publish -> List.map (fun diag -> diag.message) publish.diagnostics) (List.ofSeq client.Diagnostics)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)
