module Main.Tests.ProjectManagerTests

open Main
open Main.Tests.Common
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open SimpleTest

let private fileHasName (name: string) (f: string) =
    let parts = f.Split('/')
    let fName = parts.[parts.Length - 1]
    name = fName 

let private projectHasName (name: string) (project: string * FSharpProjectOptions) = 
  let (f, _) = project
  let parts = f.Split('/')
  let fName = parts.[parts.Length - 1]
  name = fName

let ``test find project file`` (t: TestContext) = 
    let projects = ProjectManager()
    let root = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"|] |> DirectoryInfo
    projects.AddWorkspaceRoot root 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | None -> Fail("Didn't find project file")
    | Some f -> if not (f.ProjectFileName.EndsWith "MainProject.fsproj") then Fail(f)

let ``test project-file-not-found`` (t: TestContext) = 
    let projects = ProjectManager()
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | Some f -> Fail(eprintfn "Shouldn't have found project file %s" f.ProjectFileName)
    | None -> ()

let ``test bad project file`` (t: TestContext) = 
    let projects = ProjectManager()
    let root = Path.Combine [|projectRoot.FullName; "sample"; "BadProject"|] |> DirectoryInfo
    projects.AddWorkspaceRoot root 

let ``test parse a project file recursively`` (t: TestContext) = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = ProjectParser.parseProjectOptions file
    if not (Seq.exists (fileHasName "ProjectManager.fs") parsed.SourceFiles) then Fail("Failed")
    if not (Seq.exists (projectHasName "LSP.dll") parsed.ReferencedProjects) then Fail(parsed.ReferencedProjects)
    if not (Seq.exists (fileHasName "FSharp.Compiler.Service.dll") parsed.OtherOptions) then Fail(parsed.OtherOptions)

let ``test find an fsproj in a parent dir`` (t: TestContext) = 
    let projects = ProjectManager()
    projects.AddWorkspaceRoot projectRoot
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Program.fs"|])
    let parsed = projects.FindProjectOptions(file).Value
    if not (Seq.exists (fileHasName "ProjectManager.fs") parsed.SourceFiles) then Fail("Failed")
    if not (Seq.exists (projectHasName "LSP.dll") parsed.ReferencedProjects) then Fail(parsed.ReferencedProjects)
    if not (Seq.exists (fileHasName "FSharp.Compiler.Service.dll") parsed.OtherOptions) then Fail(parsed.OtherOptions)