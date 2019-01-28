module FSharpLanguageServer.Tests.ServerTests

open FSharpLanguageServer.Tests.Common
open FSharpLanguageServer.Program
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open NUnit.Framework
open LSP.Types
open LSP
open LSP.Log
open LSP.Uris
open FSharp.Data

[<SetUp>]
let setup() = 
    LSP.Log.diagnosticsLog := stdout

[<Test>]
let ``check errors in some text``() = 
    let file = "MyScript.fsx"
    let input = """
    let foo() = "foo!""
    """
    let checker = FSharpChecker.Create()
    let projOptions, projOptionsErrors = checker.GetProjectOptionsFromScript(file, input) |> Async.RunSynchronously
    let parsingOptions, parsingOptionsErrors = checker.GetParsingOptionsFromProjectOptions(projOptions)
    let parseFileResults = checker.ParseFile(file, input, parsingOptions) |> Async.RunSynchronously
    ()

type MockClient() = 
    member val Diagnostics = System.Collections.Generic.List<PublishDiagnosticsParams>()
    interface ILanguageClient with 
        member this.PublishDiagnostics(p: PublishDiagnosticsParams): unit = 
            let file = asFile(p.uri)
            dprintfn "Received %d diagnostics for %s" p.diagnostics.Length file.Name
            this.Diagnostics.Add(p)
        member this.ShowMessage(p: ShowMessageParams): unit = 
            ()
        member this.RegisterCapability(p: RegisterCapability): unit = 
            ()
        member this.CustomNotification(method: string, p: JsonValue): unit = 
            ()

let private diagnosticMessages(client: MockClient): string list = 
    [ for publish in client.Diagnostics do 
        for diagnostic in publish.diagnostics do 
            yield diagnostic.message ]

let createServer(): MockClient * ILanguageServer = 
    let client = MockClient()
    let server = Server(client) :> ILanguageServer
    (client, server)
    
// TODO eliminate MainProject assumption
let absPath(project, file: string): string = 
    Path.Combine [|projectRoot.FullName; "sample"; project; file|]

let openFile(file: FileInfo): DidOpenTextDocumentParams = 
    {
        textDocument = 
            { 
                uri=Uri(file.FullName) 
                languageId="fsharp"
                version=0
                text=File.ReadAllText(file.FullName)
            }
    }

let initializeServer(server: ILanguageServer, sampleRootPath: string, fileName: string) =
    let file = FileInfo(Path.Combine(sampleRootPath, fileName))
    let sampleRootUri = Uri("file://" + sampleRootPath)
    server.Initialize({defaultInitializeParams with rootUri=Some sampleRootUri}) |> Async.RunSynchronously |> ignore
    server.Initialized() |> Async.RunSynchronously
    server.DidOpenTextDocument(openFile(file)) |> Async.RunSynchronously

// TODO eliminate MainProject assumption
let createServerAndReadFile(project: string, file: string): MockClient * ILanguageServer = 
    let client, server = createServer()
    let sampleRootPath = Path.Combine [|projectRoot.FullName; "sample"; project|]
    initializeServer(server, sampleRootPath, file)
    (client, server)

[<Test>]
let ``open a script file without errors``() = 
    let client, server = createServer()
    let sampleRootPath = Path.Combine [|projectRoot.FullName; "sample"; "Script"|]
    initializeServer(server, sampleRootPath, "MainScript.fsx")
    let diags = [for d in client.Diagnostics do yield! d.diagnostics]
    if not(List.isEmpty(diags)) then Assert.Fail(sprintf "%A" diags)

[<Test>]
let ``report a type error when a file is opened``() = 
    let client, server = createServerAndReadFile("MainProject", "WrongType.fs")
    if client.Diagnostics.Count = 0 then Assert.Fail("No diagnostics")
    let messages = diagnosticMessages(client)
    if not (List.exists (fun(m:string) -> m.Contains("This expression was expected to have type")) messages) then Assert.Fail(sprintf "No type error in %A" messages)

let mutable versionCounter = 1

let nextVersion() = 
    versionCounter <- versionCounter + 1 
    versionCounter

let edit(project: string, file: string, line: int, character: int, existingText: string, replacementText: string): DidChangeTextDocumentParams = 
    {
        textDocument = 
            {
                uri = Uri(absPath(project, file))
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
let save(project: string, file: string): DidSaveTextDocumentParams = 
    {
        textDocument = { uri=Uri(absPath(project, file)) }
        text = None
    }

let textDocument(project: string, file: string): TextDocumentIdentifier = 
    {
        uri=Uri(absPath(project, file))
    }

let position(line: int, character: int): Position = 
    { 
        line=line-1
        character=character-1 
    }

let textDocumentPosition(project: string, file: string, line: int, character: int): TextDocumentPositionParams = 
    {
        textDocument = textDocument(project, file)
        position = position(line, character)
    }

[<Test>]
let ``report a type error when a file is saved``() = 
    let client, server = createServerAndReadFile("MainProject", "CreateTypeError.fs")
    client.Diagnostics.Clear()
    server.DidChangeTextDocument(edit("MainProject", "CreateTypeError.fs", 4, 18, "1", "\"1\"")) |> Async.RunSynchronously
    server.DidSaveTextDocument(save("MainProject", "CreateTypeError.fs")) |> Async.RunSynchronously
    let messages = diagnosticMessages(client)
    let isTypeError(m: string) = m.Contains("This expression was expected to have type")
    if not (List.exists isTypeError messages) then Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``reference other file in same project``() = 
    let client, server = createServerAndReadFile("MainProject", "Reference.fs")
    let messages = diagnosticMessages(client)
    if not (List.exists (fun(m:string) -> m.Contains("This expression was expected to have type")) messages) then 
        Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``reference another project``() = 
    let client, server = createServerAndReadFile("MainProject", "ReferenceDependsOn.fs")
    let messages = diagnosticMessages(client)
    if not (List.exists (fun(m:string) -> m.Contains("This expression was expected to have type")) messages) then 
        Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``reference indirect dependency``() = 
    let client, server = createServerAndReadFile("MainProject", "ReferenceIndirectDep.fs")
    let messages = diagnosticMessages(client)
    if not (List.exists (fun(m:string) -> m.Contains("This expression was expected to have type")) messages) then 
        Assert.Fail(sprintf "No type error in %A" messages)

[<Test>]
let ``skip file not in project file``() = 
    let client, server = createServerAndReadFile("MainProject", "NotInFsproj.fs")
    let messages = diagnosticMessages(client)
    if not (List.exists (fun(m:string) -> m.Contains("No .fsproj or .fsx file")) messages) then 
        Assert.Fail(sprintf "No 'Not in project' error in %A" messages)

[<Test>]
let ``hover over function``() = 
    let client, server = createServerAndReadFile("MainProject", "Hover.fs")
    match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 6, 23)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")

[<Test>]
let ``hover over left edge``() = 
    let client, server = createServerAndReadFile("MainProject", "Hover.fs")
    match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 3, 13)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")

[<Test>]
let ``hover over right edge``() = 
    let client, server = createServerAndReadFile("MainProject", "Hover.fs")
    match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 3, 17)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")

[<Test>]
let ``hover over qualified name``() = 
    let client, server = createServerAndReadFile("MainProject", "Hover.fs")
    match server.Hover(textDocumentPosition("MainProject", "Hover.fs", 12, 38)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No hover")
    | Some hover -> if List.isEmpty hover.contents then Assert.Fail("Hover list is empty")

let labels(items: CompletionItem list) = 
    [for i in items do yield i.label]

[<Test>]
let ``complete List members``() = 
    let client, server = createServerAndReadFile("MainProject", "Completions.fs")
    match server.Completion(textDocumentPosition("MainProject", "Completions.fs", 4, 10)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No completions")
    | Some(completions) -> 
        CollectionAssert.Contains(labels(completions.items), "map")

[<Test>]
let ``complete result of call``() = 
    let client, server = createServerAndReadFile("MainProject", "Completions.fs")
    server.DidChangeTextDocument(edit("MainProject", "Completions.fs", 7, 16, "", ".")) |> Async.RunSynchronously
    match server.Completion(textDocumentPosition("MainProject", "Completions.fs", 7, 17)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No completions")
    | Some(completions) -> 
        CollectionAssert.Contains(labels(completions.items), "IsSome")

[<Test>]
let ``complete name with space``() = 
    let client, server = createServerAndReadFile("MainProject", "Completions.fs")
    let completions = 
        match server.Completion(textDocumentPosition("MainProject", "Completions.fs", 13, 7)) |> Async.RunSynchronously with 
        | None -> []
        | Some(completions) -> completions.items
    let insertText = 
        [ for c in completions do 
            if c.label.Contains("name with space") then
                yield c.insertText]
    CollectionAssert.Contains(insertText, Some("``name with space``"))

// [<Test>] TODO
let ``dont complete inside a string``() = 
    let client, server = createServerAndReadFile("MainProject", "CompleteInString.fs")
    match server.Completion(textDocumentPosition("MainProject", "CompleteInString.fs", 3, 15)) |> Async.RunSynchronously with 
    | None -> ()
    | Some(completions) -> 
        Assert.Fail(sprintf "Should not have completed in string: %A" completions)

[<Test>]
let ``signature help``() = 
    let client, server = createServerAndReadFile("MainProject", "SignatureHelp.fs")
    match server.SignatureHelp(textDocumentPosition("MainProject", "SignatureHelp.fs", 3, 47)) |> Async.RunSynchronously with 
    | None -> Assert.Fail("No signature help")
    | Some help -> 
        if List.isEmpty help.signatures then Assert.Fail("Signature list is empty")
        if List.length help.signatures <> 2 then Assert.Fail(sprintf "Expected 2 overloads of Substring but found %A" help)

[<Test>]
let ``test findMethodCallBeforeCursor``() = 
    match findMethodCallBeforeCursor("foo()", 4) with 
    | None -> Assert.Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Assert.Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor("foo ()", 5) with 
    | None -> Assert.Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Assert.Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor("foo(bar(), )", 11) with 
    | None -> Assert.Fail("Should have found foo")
    | Some 3 -> ()
    | Some i -> Assert.Fail(sprintf "End of foo is at 3 but found %d" i)
    match findMethodCallBeforeCursor("let foo()", 9) with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in let expression" i)
    match findMethodCallBeforeCursor("let private foo ()", 17) with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in let expression" i)
    match findMethodCallBeforeCursor("member foo ()", 12) with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in member expression" i)
    match findMethodCallBeforeCursor("member this.foo ()", 17) with 
    | None -> ()
    | Some i -> Assert.Fail(sprintf "Shouldn't find method %d in member expression" i)
    match findMethodCallBeforeCursor("let foo () = bar()", 17) with 
    | None -> Assert.Fail("Should have found bar")
    | Some 16 -> ()
    | Some i -> Assert.Fail(sprintf "End of bar is at 17 but found %d" i)

[<Test>]
let ``find document symbols``() = 
    let client, server = createServerAndReadFile("MainProject", "Reference.fs")
    let found = server.DocumentSymbols({textDocument=textDocument("MainProject", "Reference.fs")}) |> Async.RunSynchronously
    let names = found |> List.map (fun f -> f.name)
    if not (List.contains "Reference" names) then Assert.Fail(sprintf "Reference is not in %A" names)
    if List.contains "ReferenceDependsOn" names then Assert.Fail("Document symbols includes dependency")

[<Test>]
let ``find interface inside module``() = 
    let client, server = createServerAndReadFile("MainProject", "InterfaceInModule.fs")
    let found = server.DocumentSymbols({textDocument=textDocument("MainProject", "InterfaceInModule.fs")}) |> Async.RunSynchronously
    let names = found |> List.map (fun f -> f.name)
    if not (List.contains "IMyInterface" names) then Assert.Fail(sprintf "IMyInterface is not in %A" names)

[<Test>]
let ``find project symbols``() = 
    let client, server = createServerAndReadFile("MainProject", "SignatureHelp.fs")
    let found = server.WorkspaceSymbols({query = "signatureHelp"}) |> Async.RunSynchronously
    if List.isEmpty found then Assert.Fail("Should have found signatureHelp")
    let found = server.WorkspaceSymbols({query = "IndirectLibrary"}) |> Async.RunSynchronously
    if List.isEmpty found then Assert.Fail("Should have found IndirectLibrary")
    let found = server.WorkspaceSymbols({query = "IMyInterface"}) |> Async.RunSynchronously
    if List.isEmpty found then Assert.Fail("Should have found IMyInterface")

[<Test>]
let ``go to definition``() = 
    let client, server = createServerAndReadFile("MainProject", "Reference.fs")
    match server.GotoDefinition(textDocumentPosition("MainProject", "Reference.fs", 3, 30)) |> Async.RunSynchronously with 
    | [] -> Assert.Fail("No symbol definition")
    | [single] -> ()
    | many -> Assert.Fail(sprintf "Multiple definitions found %A" many)

[<Test>]
let ``find references``() = 
    let client, server = createServerAndReadFile("MainProject", "DeclareSymbol.fs")
    let p = 
        {
            textDocument = textDocument("MainProject", "DeclareSymbol.fs")
            position = { line=3-1; character=5-1 }
            context = { includeDeclaration=true }
        }
    let list = server.FindReferences(p) |> Async.RunSynchronously
    let isReferenceFs(r: Location) = r.uri.OriginalString.EndsWith("UseSymbol.fs")
    let found = List.exists isReferenceFs list
    if not found then Assert.Fail(sprintf "Didn't find reference from UseSymbol.fs in %A" list)

[<Test>]
let ``rename across files``() = 
    let client, server = createServerAndReadFile("MainProject", "RenameTarget.fs")
    let p = {
        textDocument=textDocument("MainProject", "RenameTarget.fs")
        position=position(3, 11)
        newName = "renamedSymbol" 
    }
    let edit = server.Rename(p) |> Async.RunSynchronously
    let ranges = [
        for doc in edit.documentChanges do 
            for e in doc.edits do 
                let file = asFile(doc.textDocument.uri).Name
                yield file, e.range.start.line + 1, e.range.start.character + 1, e.range.``end``.character + 1 ]
    if not (List.contains ("RenameReference.fs", 3, 45, 59) ranges) then Assert.Fail(sprintf "%A" ranges)

[<Test>]
let ``match title case queries``() = 
    Assert.True(matchesTitleCase("fb", "FooBar"))
    Assert.True(matchesTitleCase("fob", "FooBar"))
    Assert.True(matchesTitleCase("fb", "AnyPrefixFooBar"))
    Assert.True(matchesTitleCase("fb", "UPPERFooBar"))
    Assert.False(matchesTitleCase("fb", "Foobar"))

[<Test>]
let ``create Run Test code lens``() = 
    let client, server = createServerAndReadFile("HasTests", "MyTests.fs")
    let lenses = server.CodeLens({ textDocument = textDocument("HasTests", "MyTests.fs") }) |> Async.RunSynchronously
    let lines = [for l in lenses do yield l.range.start.line]
    CollectionAssert.Contains(lines, 5, sprintf "No line 5 in %A" lenses)

[<Test>]
let ``Implementation code lens``() = 
    let client, server = createServerAndReadFile("Signature", "HasSignature.fsi")
    let lenses = server.CodeLens({ textDocument = textDocument("Signature", "HasSignature.fsi") }) |> Async.RunSynchronously
    let lens = [for l in lenses do if l.range.start.line = 2 then yield l].Head
    let resolve = server.ResolveCodeLens(lens) |> Async.RunSynchronously
    if resolve.command.IsNone then 
        Assert.Fail(sprintf "No resolved command in %A" resolve)

[<Test>]
let ``nested Implementation code lens``() = 
    let client, server = createServerAndReadFile("Signature", "HasSignature.fsi")
    let lenses = server.CodeLens({ textDocument = textDocument("Signature", "HasSignature.fsi") }) |> Async.RunSynchronously
    let lens = [for l in lenses do if l.range.start.line = 5 then yield l].Head 
    let resolve = server.ResolveCodeLens(lens) |> Async.RunSynchronously
    if resolve.command.IsNone then 
        Assert.Fail(sprintf "No resolved command in %A" resolve)

[<Test>]
let ``missing Implementation code lens``() = 
    let client, server = createServerAndReadFile("Signature", "HasSignature.fsi")
    let lenses = server.CodeLens({ textDocument = textDocument("Signature", "HasSignature.fsi") }) |> Async.RunSynchronously
    let lens = [for l in lenses do if l.range.start.line = 6 then yield l].Head 
    let resolve = server.ResolveCodeLens(lens) |> Async.RunSynchronously
    if resolve.command.IsNone then 
        Assert.Fail(sprintf "No resolved command in %A" resolve)

[<Test>]
let ``overloaded method Implementation code lens``() = 
    let client, server = createServerAndReadFile("Signature", "HasSignature.fsi")
    let lenses = server.CodeLens({ textDocument = textDocument("Signature", "HasSignature.fsi") }) |> Async.RunSynchronously
    let lens = [for l in lenses do if l.range.start.line = 10 then yield l].Head 
    let resolve = server.ResolveCodeLens(lens) |> Async.RunSynchronously
    if resolve.command.IsNone then 
        Assert.Fail(sprintf "No resolved command in %A" resolve)

[<Test>]
let ``report no type errors in CSharp reference``() = 
    let client, server = createServerAndReadFile("ReferenceCSharp", "Library.fs")
    let messages = diagnosticMessages(client)
    CollectionAssert.IsEmpty(messages)