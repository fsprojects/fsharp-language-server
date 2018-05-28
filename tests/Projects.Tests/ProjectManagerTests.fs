module Projects.Tests.ProjectManagerTests

open Projects
open Projects.Tests.Common
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open NUnit.Framework
open LSP.Types 
open LSP.Json

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

let private fileHasName (name: string) (f: FileInfo) = f.Name = name

let private endsWith (name: string) (f: string) = f.EndsWith name

[<Test>]
let ``find project file`` () = 
    let projects = ProjectManager(MockClient())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot(root))
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions(file)
    match project with 
    | Error(m) -> Assert.Fail(m)
    | Ok(f) -> if not(f.ProjectFileName.EndsWith "MainProject.fsproj") then Assert.Fail(sprintf "%A" f)

[<Test>]
let ``find an local dll`` () = 
    let projects = ProjectManager(MockClient())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot(root))
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"; "Program.fs"|])
    match projects.FindProjectOptions(file) with 
    | Error(m) -> Assert.Fail m
    | Ok(parsed) -> if not (Seq.exists (endsWith "LocalDll.dll") parsed.OtherOptions) then Assert.Fail(sprintf "%A" parsed.OtherOptions)

[<Test>]
let ``project-file-not-found`` () = 
    let projects = ProjectManager(MockClient())
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | Ok(f) -> Assert.Fail(sprintf "Shouldn't have found project file %s" f.ProjectFileName)
    | Error(m) -> ()

[<Test>]
let ``bad project file`` () = 
    let projects = ProjectManager(MockClient())
    let root = Path.Combine [|projectRoot.FullName; "sample"; "BadProject"|] |> DirectoryInfo
    Async.RunSynchronously(projects.AddWorkspaceRoot root)

[<Test>]
let ``find an fsproj in a parent dir`` () = 
    let projects = ProjectManager(MockClient())
    Async.RunSynchronously(projects.AddWorkspaceRoot(projectRoot))
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Projects"; "ProjectManager.fs"|])
    let parsed = match projects.FindProjectOptions(file) with Ok p -> p
    if not (Seq.exists (endsWith "ProjectManager.fs") parsed.SourceFiles) then Assert.Fail("Failed")
    if not (Seq.exists (fst >> endsWith "LSP.dll") parsed.ReferencedProjects) then Assert.Fail(sprintf "%A" parsed.ReferencedProjects)
    if not (Seq.exists (endsWith "FSharp.Compiler.Service.dll") parsed.OtherOptions) then Assert.Fail(sprintf "%A" parsed.OtherOptions)