module Main.Tests.ProjectManagerTests

open Main
open Main.Tests.Common
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open SimpleTest
open Log

let private fileHasName (name: string) (f: FileInfo) = f.Name = name

let private endsWith (name: string) (f: string) = f.EndsWith name

let ``test find project file`` (t: TestContext) = 
    let projects = ProjectManager()
    let root = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"|] |> DirectoryInfo
    projects.AddWorkspaceRoot root 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | Error m -> Fail m
    | Ok f -> if not (f.ProjectFileName.EndsWith "MainProject.fsproj") then Fail(f)

let ``test project-file-not-found`` (t: TestContext) = 
    let projects = ProjectManager()
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | Ok f -> Fail(log "Shouldn't have found project file %s" f.ProjectFileName)
    | Error m -> ()

let ``test bad project file`` (t: TestContext) = 
    let projects = ProjectManager()
    let root = Path.Combine [|projectRoot.FullName; "sample"; "BadProject"|] |> DirectoryInfo
    projects.AddWorkspaceRoot root 

let ``test parse a project file with dependencies`` (t: TestContext) = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = match ProjectParser.parseFsProj file with Ok p -> p
    if not (Seq.exists (fileHasName "ProjectManager.fs") parsed.compileInclude) then Fail("Failed")
    if not (Seq.exists (fileHasName "LSP.fsproj") parsed.projectReferenceInclude) then Fail(parsed.projectReferenceInclude)

let ``test find an fsproj in a parent dir`` (t: TestContext) = 
    let projects = ProjectManager()
    projects.AddWorkspaceRoot projectRoot
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Program.fs"|])
    let parsed = match projects.FindProjectOptions(file) with Ok p -> p
    if not (Seq.exists (endsWith "ProjectManager.fs") parsed.SourceFiles) then Fail("Failed")
    if not (Seq.exists (fst >> endsWith "LSP.dll") parsed.ReferencedProjects) then Fail(parsed.ReferencedProjects)
    if not (Seq.exists (endsWith "FSharp.Compiler.Service.dll") parsed.OtherOptions) then Fail(parsed.OtherOptions)