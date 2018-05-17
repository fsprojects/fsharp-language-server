module Main.Tests.ProjectManagerTests

open Main
open Main.Tests.Common
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open NUnit.Framework

[<SetUp>]
let setup() = 
    LSP.Log.diagnosticsLog := stdout

let private fileHasName (name: string) (f: FileInfo) = f.Name = name

let private endsWith (name: string) (f: string) = f.EndsWith name

[<Test>]
let ``find project file`` () = 
    let projects = ProjectManager()
    let root = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"|] |> DirectoryInfo
    projects.AddWorkspaceRoot root 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | Error m -> Assert.Fail m
    | Ok f -> if not (f.ProjectFileName.EndsWith "MainProject.fsproj") then Assert.Fail(sprintf "%A" f)

[<Test>]
let ``project-file-not-found`` () = 
    let projects = ProjectManager()
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
    let project = projects.FindProjectOptions file
    match project with 
    | Ok f -> Assert.Fail(sprintf "Shouldn't have found project file %s" f.ProjectFileName)
    | Error m -> ()

[<Test>]
let ``bad project file`` () = 
    let projects = ProjectManager()
    let root = Path.Combine [|projectRoot.FullName; "sample"; "BadProject"|] |> DirectoryInfo
    projects.AddWorkspaceRoot root 

[<Test>]
let ``parse a project file with dependencies`` () = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = match ProjectParser.parseFsProj file with Ok p -> p
    if not (Seq.exists (fileHasName "ProjectManager.fs") parsed.compileInclude) then Assert.Fail(sprintf "%A" parsed.compileInclude)
    if not (Seq.exists (fileHasName "LSP.fsproj") parsed.projectReferenceInclude) then Assert.Fail(sprintf "%A" parsed.projectReferenceInclude)

[<Test>]
let ``find an fsproj in a parent dir`` () = 
    let projects = ProjectManager()
    projects.AddWorkspaceRoot projectRoot
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Program.fs"|])
    let parsed = match projects.FindProjectOptions(file) with Ok p -> p
    if not (Seq.exists (endsWith "ProjectManager.fs") parsed.SourceFiles) then Assert.Fail("Failed")
    if not (Seq.exists (fst >> endsWith "LSP.dll") parsed.ReferencedProjects) then Assert.Fail(sprintf "%A" parsed.ReferencedProjects)
    if not (Seq.exists (endsWith "FSharp.Compiler.Service.dll") parsed.OtherOptions) then Assert.Fail(sprintf "%A" parsed.OtherOptions)