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

let ``test parse a project file recursively`` (t: TestContext) = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = ProjectParser.parseProjectOptions file
    if not (Seq.exists (fileHasName "ProjectManager.fs") parsed.SourceFiles) then Fail("Failed")
    if not (Seq.exists (projectHasName "LSP.fsproj.dll") parsed.ReferencedProjects) then Fail(parsed.ReferencedProjects)
    // if not (Seq.exists (hasName "FSharp.Compiler.Service.dll") parsed.references)

let ``test find an fsproj in a parent dir`` (t: TestContext) = 
    let projects = ProjectManager()
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Program.fs"|])
    let parsed = projects.FindProjectOptions(Uri(file.FullName)) |> Option.get
    if not (Seq.exists (fileHasName "ProjectManager.fs") parsed.SourceFiles) then Fail("Failed")
    if not (Seq.exists (projectHasName "LSP.fsproj.dll") parsed.ReferencedProjects) then Fail(parsed.ReferencedProjects)
    // if not (Seq.exists (hasName "FSharp.Compiler.Service.dll") parsed.references)