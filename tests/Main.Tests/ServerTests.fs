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
    member val Diagnostics = System.Collections.Generic.List<PublishDiagnosticsParams>()
    interface ILanguageClient with 
        member this.PublishDiagnostics (p: PublishDiagnosticsParams): unit = 
            this.Diagnostics.Add(p)

let private diagnosticMessages (client: MockClient): list<string> = 
    List.collect (fun publish -> List.map (fun diag -> diag.message) publish.diagnostics) (List.ofSeq client.Diagnostics)

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
    let messages = diagnosticMessages(client)
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

let position (file: string) (line: int) (character: int): TextDocumentPositionParams = 
    {
        textDocument = { uri=Uri(absPath file) }
        position = { line=line-1; character=character-1 }
    }

let ``test report a type error when a file is saved`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "CreateTypeError.fs"
    client.Diagnostics.Clear()
    server.DidChangeTextDocument(edit "CreateTypeError.fs" 4 18 "1" "\"1\"")
    server.DidSaveTextDocument(save "CreateTypeError.fs")
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test reference other file in same project`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "Reference.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test reference another project`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "ReferenceDependsOn.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test reference indirect dependency`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "ReferenceIndirectDep.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Fail(sprintf "No type error in %A" messages)

let ``test skip file not in project file`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "NotInFsproj.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("Not in project")) messages) then Fail(sprintf "No 'Not in project' error in %A" messages)

let ``test findNamesUnderCursor`` (t: TestContext) = 
    let names = findNamesUnderCursor "foo" 2
    if names <> ["foo"] then Fail(names)
    let names = findNamesUnderCursor "foo.bar" 6
    if names <> ["foo"; "bar"] then Fail(names)
    let names = findNamesUnderCursor "let x = foo.bar" 14
    if names <> ["foo"; "bar"] then Fail(names)
    let names = findNamesUnderCursor "let x = foo.bar" 10
    if names <> ["foo"] then Fail(names)
    let names = findNamesUnderCursor "let x = ``foo bar``.bar" 22
    if names <> ["foo bar"; "bar"] then Fail(names)

let ``test hover over function`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "Hover.fs"
    match server.Hover(position "Hover.fs" 6 23) with 
    | None -> Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Fail("Hover list is empty")

let ``test hover over qualified name`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "Hover.fs"
    match server.Hover(position "Hover.fs" 12 38) with 
    | None -> Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Fail("Hover list is empty")

let ``test complete List members`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "Completions.fs"
    match server.Completion(position "Completions.fs" 2 10) with 
    | None -> Fail("No completions")
    | Some completions -> 
        if List.isEmpty completions.items then Fail("Completion list is empty")
        let labels = List.map (fun (i:CompletionItem) -> i.label) completions.items 
        if not (List.contains "map" labels) then Fail(sprintf "List.map is not in %A" labels)

let ``test signature help`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "SignatureHelp.fs"
    match server.SignatureHelp(position "SignatureHelp.fs" 3 47) with 
    | None -> Fail("No signature help")
    | Some help -> 
        if List.isEmpty help.signatures then Fail("Signature list is empty")
        if List.length help.signatures <> 2 then Fail(sprintf "Expected 2 overloads of Substring but found %A" help)

let ``test findMethodCallBeforeCursor`` (t: TestContext) = 
    match findMethodCallBeforeCursor "foo()" 4 with 
    | None -> Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor "foo ()" 5 with 
    | None -> Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor "foo(bar(), )" 11 with 
    | None -> Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor "let foo ()" 9 with 
    | None -> ()
    | Some i -> Fail(sprintf "Shouldn't find method %d in let expression" i)
    match findMethodCallBeforeCursor "let private foo ()" 17 with 
    | None -> ()
    | Some i -> Fail(sprintf "Shouldn't find method %d in let expression" i)
    match findMethodCallBeforeCursor "member foo ()" 12 with 
    | None -> ()
    | Some i -> Fail(sprintf "Shouldn't find method %d in member expression" i)
    match findMethodCallBeforeCursor "member this.foo ()" 17 with 
    | None -> ()
    | Some i -> Fail(sprintf "Shouldn't find method %d in member expression" i)
    match findMethodCallBeforeCursor "let foo () = bar()" 17 with 
    | None -> Fail("Should have found bar")
    | Some 16 -> ()
    | Some i -> Fail(sprintf "End of bar is at 17 but found %d" i)

let ``test find project symbols`` (t: TestContext) = 
    let (client, server) = createServerAndReadFile "SignatureHelp.fs"
    let found = server.WorkspaceSymbols({query = "signatureHelp"})
    if List.isEmpty found then Fail("Should have found signatureHelp")