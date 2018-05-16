module Main.Tests.ServerTests

open Main.Tests.Common
open Main.Program
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open System.Reflection
open System.Diagnostics
open NUnit.Framework
open LSP.Types
open LSP
open LSP.Log

[<SetUp>]
let setup () = 
    LSP.Log.diagnosticsLog := stdout

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
    ()

type MockClient() = 
    member val Diagnostics = System.Collections.Generic.List<PublishDiagnosticsParams>()
    interface ILanguageClient with 
        member this.PublishDiagnostics (p: PublishDiagnosticsParams): unit = 
            dprintfn "Received %d diagnostics for %s" p.diagnostics.Length (FileInfo(p.uri.AbsolutePath).Name)
            this.Diagnostics.Add(p)
        member this.RegisterCapability (p: RegisterCapability): unit = 
            ()

let private diagnosticMessages (client: MockClient): string list = 
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
    
let openFile (name: string): DidOpenTextDocumentParams = 
    let (file, fileText) = readFile name
    {
        textDocument = 
            { 
                uri=Uri(absPath file) 
                languageId="fsharp"
                version=0
                text=fileText
            }
    }

let createServerAndReadFile (name: string): MockClient * ILanguageServer = 
    let (client, server) = createServer()
    let sampleRootPath = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"|]
    let sampleRootUri = Uri("file://" + sampleRootPath)
    server.Initialize({defaultInitializeParams with rootUri=Some sampleRootUri}) |> Async.RunSynchronously |> ignore
    server.DidOpenTextDocument(openFile name) |> Async.RunSynchronously
    (client, server)

[<Test>]
let ``report a type error when a file is opened`` () = 
    let (client, server) = createServerAndReadFile "WrongType.fs"
    if client.Diagnostics.Count = 0 then Assert.Fail("No diagnostics")
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Assert.Fail(sprintf "No type error in %A" messages)

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

let textDocument (file: string): TextDocumentIdentifier = 
    {
        uri=Uri(absPath file)
    }

let position (line: int) (character: int): Position = 
    { 
        line=line-1
        character=character-1 
    }

let textDocumentPosition (file: string) (line: int) (character: int): TextDocumentPositionParams = 
    {
        textDocument = textDocument file
        position = position line character
    }

[<Test>]
let ``report a type error when a file is saved`` () = 
    let (client, server) = createServerAndReadFile "CreateTypeError.fs"
    client.Diagnostics.Clear()
    server.DidChangeTextDocument(edit "CreateTypeError.fs" 4 18 "1" "\"1\"") |> Async.RunSynchronously
    server.DidSaveTextDocument(save "CreateTypeError.fs") |> Async.RunSynchronously
    let messages = diagnosticMessages(client)
    let isTypeError (m: string) = m.Contains("This expression was expected to have type")
    if not (List.exists isTypeError messages) then Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``report a type error when a referenced file is changed`` () = 
    let (client, server) = createServerAndReadFile "BreakParentReference.fs"
    server.DidOpenTextDocument(openFile "BreakParentTarget.fs") |> Async.RunSynchronously
    client.Diagnostics.Clear()
    server.DidChangeTextDocument(edit "BreakParentTarget.fs" 3 17 "1" "\"1\"") |> Async.RunSynchronously
    server.DidSaveTextDocument(save "BreakParentTarget.fs") |> Async.RunSynchronously
    let files = [for group in client.Diagnostics do yield group.uri]
    let isBreakParent (uri: Uri) = uri.AbsolutePath.EndsWith "BreakParentReference.fs"
    if not (List.exists isBreakParent files) then Assert.Fail(sprintf "Didn't lint BreakParentReference.fs in %A" files)

[<Test>]
let ``reference other file in same project`` () = 
    let (client, server) = createServerAndReadFile "Reference.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``reference another project`` () = 
    let (client, server) = createServerAndReadFile "ReferenceDependsOn.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``reference indirect dependency`` () = 
    let (client, server) = createServerAndReadFile "ReferenceIndirectDep.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("This expression was expected to have type")) messages) then Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``skip file not in project file`` () = 
    let (client, server) = createServerAndReadFile "NotInFsproj.fs"
    let messages = diagnosticMessages(client)
    if not (List.exists (fun (m:string) -> m.Contains("No .fsproj file")) messages) then Assert.Fail(sprintf "No 'Not in project' error in %A" messages)

[<Test>]
let ``findNamesUnderCursor`` () = 
    let names = findNamesUnderCursor "foo" 2
    Assert.AreEqual(["foo"], names)
    let names = findNamesUnderCursor "foo.bar" 6
    Assert.AreEqual(["foo"; "bar"], names)
    let names = findNamesUnderCursor "let x = foo.bar" 14
    Assert.AreEqual(["foo"; "bar"], names)
    let names = findNamesUnderCursor "let x = foo.bar" 10
    Assert.AreEqual(["foo"], names)
    let names = findNamesUnderCursor "let x = ``foo bar``.bar" 22
    Assert.AreEqual(["foo bar"; "bar"], names)

[<Test>]
let ``hover over function`` () = 
    let (client, server) = createServerAndReadFile "Hover.fs"
    match server.Hover(textDocumentPosition "Hover.fs" 6 23) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")

[<Test>]
let ``hover over qualified name`` () = 
    let (client, server) = createServerAndReadFile "Hover.fs"
    match server.Hover(textDocumentPosition "Hover.fs" 12 38) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")

[<Test>]
let ``complete List members`` () = 
    let (client, server) = createServerAndReadFile "Completions.fs"
    match server.Completion(textDocumentPosition "Completions.fs" 2 10) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No completions")
    | Some completions -> 
        if List.isEmpty completions.items then Assert.Fail("Completion list is empty")
        let labels = List.map (fun (i:CompletionItem) -> i.label) completions.items 
        if not (List.contains "map" labels) then Assert.Fail(sprintf "List.map is not in %A" labels)

[<Test>]
let ``signature help`` () = 
    let (client, server) = createServerAndReadFile "SignatureHelp.fs"
    match server.SignatureHelp(textDocumentPosition "SignatureHelp.fs" 3 47) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No signature help")
    | Some help -> 
        if List.isEmpty help.signatures then Assert.Fail("Signature list is empty")
        if List.length help.signatures <> 2 then Assert.Fail(sprintf "Expected 2 overloads of Substring but found %A" help)

[<Test>]
let ``findMethodCallBeforeCursor`` () = 
    match findMethodCallBeforeCursor "foo()" 4 with 
    | None -> Assert.Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Assert.Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor "foo ()" 5 with 
    | None -> Assert.Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Assert.Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor "foo(bar(), )" 11 with 
    | None -> Assert.Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Assert.Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor "let foo ()" 9 with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in let expression" i)
    match findMethodCallBeforeCursor "let private foo ()" 17 with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in let expression" i)
    match findMethodCallBeforeCursor "member foo ()" 12 with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in member expression" i)
    match findMethodCallBeforeCursor "member this.foo ()" 17 with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in member expression" i)
    match findMethodCallBeforeCursor "let foo () = bar()" 17 with 
    | None -> Assert.Fail("Should have found bar")
    | Some 16 -> ()
    | Some i -> Assert.Fail(sprintf "End of bar is at 17 but found %d" i)

[<Test>]
let ``find document symbols`` () = 
    let (client, server) = createServerAndReadFile "Reference.fs"
    let found = server.DocumentSymbols({textDocument=textDocument "Reference.fs"}) |> Async.RunSynchronously
    let names = found |> List.map (fun f -> f.name)
    if not (List.contains "Reference" names) then Assert.Fail(sprintf "Reference is not in %A" names)
    if List.contains "ReferenceDependsOn" names then Assert.Fail("Document symbols includes dependency")

[<Test>]
let ``find interface inside module`` () = 
    let (client, server) = createServerAndReadFile "InterfaceInModule.fs"
    let found = server.DocumentSymbols({textDocument=textDocument "InterfaceInModule.fs"}) |> Async.RunSynchronously
    let names = found |> List.map (fun f -> f.name)
    if not (List.contains "IMyInterface" names) then Assert.Fail(sprintf "IMyInterface is not in %A" names)

[<Test>]
let ``find project symbols`` () = 
    let (client, server) = createServerAndReadFile "SignatureHelp.fs"
    let found = server.WorkspaceSymbols({query = "signatureHelp"}) |> Async.RunSynchronously
    if List.isEmpty found then Assert.Fail("Should have found signatureHelp")
    let found = server.WorkspaceSymbols({query = "IndirectLibrary"}) |> Async.RunSynchronously
    if List.isEmpty found then Assert.Fail("Should have found IndirectLibrary")
    let found = server.WorkspaceSymbols({query = "IMyInterface"}) |> Async.RunSynchronously
    if List.isEmpty found then Assert.Fail("Should have found IMyInterface")

[<Test>]
let ``go to definition`` () = 
    let (client, server) = createServerAndReadFile "Reference.fs"
    match server.GotoDefinition(textDocumentPosition "Reference.fs" 3 31) |> Async.RunSynchronously with 
    | [] -> Assert.Fail("No symbol definition")
    | [single] -> ()
    | many -> Assert.Fail(sprintf "Multiple definitions found %A" many)

[<Test>]
let ``find references`` () = 
    let (client, server) = createServerAndReadFile "DeclareSymbol.fs"
    let p = 
        {
            textDocument = textDocument "DeclareSymbol.fs"
            position = { line=3-1; character=6-1 }
            context = { includeDeclaration=true }
        }
    let list = server.FindReferences(p) |> Async.RunSynchronously
    let isReferenceFs (r: Location) = r.uri.AbsolutePath.EndsWith("UseSymbol.fs")
    let found = List.exists isReferenceFs list
    if not found then Assert.Fail(sprintf "Didn't find reference from UseSymbol.fs in %A" list)

[<Test>]
let ``rename across files`` () = 
    let (client, server) = createServerAndReadFile "RenameTarget.fs"
    let p = {
        textDocument=textDocument "RenameTarget.fs"
        position=position 3 11
        newName = "renamedSymbol" 
    }
    let edit = server.Rename(p) |> Async.RunSynchronously
    let ranges = [
        for doc in edit.documentChanges do 
            for e in doc.edits do 
                let file = FileInfo(doc.textDocument.uri.AbsolutePath).Name
                yield file, e.range.start.line + 1, e.range.start.character + 1, e.range.``end``.character + 1 ]
    if not (List.contains ("RenameReference.fs", 3, 45, 59) ranges) then Assert.Fail(sprintf "%A" ranges)