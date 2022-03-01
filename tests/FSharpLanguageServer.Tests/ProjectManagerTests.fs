module FSharpLanguageServer.Tests.ProjectManagerTests

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharpLanguageServer.Tests.Common
open FSharpLanguageServer
open System
open System.IO
open NUnit.Framework
open LSP.Types 
open FSharp.Data
open FSharp.Compiler.CodeAnalysis

type MockClient() = 
    member val Diagnostics = System.Collections.Generic.List<PublishDiagnosticsParams>()
    interface ILanguageClient with 
        member this.PublishDiagnostics(p: PublishDiagnosticsParams): unit = 
            ()
        member this.ShowMessage(p: ShowMessageParams): unit = 
            ()
        member this.RegisterCapability(p: RegisterCapability): unit = 
            ()
        member this.CustomNotification(method: string, p: JsonValue): unit = 
            ()

[<SetUp>]
let setup() = 
    LSP.Log.diagnosticsLog := stdout

[<Test>]
let ``find project file``() = 
    let projects = ProjectManager(FSharpChecker.Create())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot(root))
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions(file)
    match project with 
    | Error(m) -> Assert.Fail(sprintf "%A" m)
    | Ok(f) -> if not(f.ProjectFileName.EndsWith "MainProject.fsproj") then Assert.Fail(sprintf "%A" f)

[<Test>]
let ``choose fsproj referenced by sln``() = 
    let projects = ProjectManager(FSharpChecker.Create())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "SlnReferences"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot(root))
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "SlnReferences"; "Main.fs"|])
    let project = projects.FindProjectOptions(file)
    match project with 
    | Error(m) -> Assert.Fail(sprintf "%A" m)
    | Ok(f) -> if not(f.ProjectFileName.EndsWith "ReferencedProject.fsproj") then Assert.Fail(sprintf "%A" f)

[<Test>]
let ``find script file``() = 
    let projects = ProjectManager(FSharpChecker.Create())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "Script"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot(root))
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "Script"; "LoadedByScript.fs"|])
    let project = projects.FindProjectOptions(file)
    match project with 
    | Error(m) -> Assert.Fail(sprintf "%A" m)
    | Ok(f) -> if not(f.ProjectFileName.EndsWith("MainScript.fsx.fsproj")) then Assert.Fail(sprintf "%A" f)

[<Test>]
let ``find an local dll``() = 
    let projects = ProjectManager(FSharpChecker.Create())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot(root))
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"; "Program.fs"|])
    match projects.FindProjectOptions(file) with 
    | Error(m) -> Assert.Fail(sprintf "%A" m)
    | Ok(parsed) ->
        let isLocalDll(s: string) = s.EndsWith("IndirectDep.dll")
        if not (Seq.exists isLocalDll parsed.OtherOptions) then Assert.Fail(sprintf "%A" parsed.OtherOptions)

[<Test>]
let ``project-file-not-found``() = 
    let projects = ProjectManager(FSharpChecker.Create())
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | Ok(f) -> Assert.Fail(sprintf "Shouldn't have found project file %s" f.ProjectFileName)
    | Error(_) -> ()

[<Test>]
let ``bad project file``() = 
    let projects = ProjectManager(FSharpChecker.Create())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "BadProject"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot root)

[<Test>]
let ``get script options``() = 
    let projects = ProjectManager(FSharpChecker.Create())
    let script = Path.Combine [|projectRoot.FullName; "sample"; "Script"; "MainScript.fsx"|] |> FileInfo 
    projects.NewProjectFile(script)
    match projects.FindProjectOptions(script) with 
    | Error(m) -> Assert.Fail(sprintf "%A" m)
    | Ok(options) -> 
        let references = [for o in options.OtherOptions do if o.StartsWith("-r:") then yield FileInfo(o.Substring("-r:".Length)).Name]
        CollectionAssert.Contains(references, "FSharp.Core.dll")
        CollectionAssert.Contains(references, "System.Runtime.dll")