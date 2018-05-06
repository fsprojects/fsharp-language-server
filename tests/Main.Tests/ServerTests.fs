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

let absPath (file: string): string = 
    Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; file|]

let readFile (name: string): string * string = 
    let file = absPath name
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

let mutable versionCounter = 1

let nextVersion () = 
    versionCounter <- versionCounter + 1 
    versionCounter

let edit (file: string) (line: int) (character: int) (existingText: string) (replacementText: string): DidChangeTextDocumentParams = 
    {
        textDocument = 
            {
                uri = Uri(absPath file)
                version = nextVersion()
            }
        contentChanges = 
            [
                {
                    range = Some
                        {
                            start = { line=line-1; character=character-1 }
                            ``end`` = { line=line-1; character=character-1+existingText.Length }
                        }
                    rangeLength = None
                    text = replacementText
                }
            ]
    }
let save (file: string): DidSaveTextDocumentParams = 
    {
        textDocument = { uri=Uri(absPath file) }
        text = None
    }

let ``test report a type error when a file is saved`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "CreateTypeError.fs"
    client.Diagnostics.Clear()
    server.DidChangeTextDocument(edit "CreateTypeError.fs" 4 18 "1" "\"1\"")
    server.DidSaveTextDocument(save "CreateTypeError.fs")
    let messages = List.collect (fun publish -> List.map (fun diag -> diag.message) publish.diagnostics) (List.ofSeq client.Diagnostics)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test reference other file in same project`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "Reference.fs"
    let messages = List.collect (fun publish -> List.map (fun diag -> diag.message) publish.diagnostics) (List.ofSeq client.Diagnostics)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test reference another project`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "ReferenceDependsOn.fs"
    let messages = List.collect (fun publish -> List.map (fun diag -> diag.message) publish.diagnostics) (List.ofSeq client.Diagnostics)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test reference indirect dependency`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "ReferenceIndirectDep.fs"
    let messages = List.collect (fun publish -> List.map (fun diag -> diag.message) publish.diagnostics) (List.ofSeq client.Diagnostics)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test skip file not in project file`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "NotInFsproj.fs"
    let messages = List.collect (fun publish -> List.map (fun diag -> diag.message) publish.diagnostics) (List.ofSeq client.Diagnostics)
    if not (List.exists (fun (m:string) -> m.Contains("Not in project")) messages) then Fail(sprintf "No 'Not in project' error in %A" messages)
