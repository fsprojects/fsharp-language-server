module Main.Tests.ProjectManagerTests

open Main
open Main.Tests.Common
open System
open System.IO
open Xunit
open Microsoft.FSharp.Compiler.SourceCodeServices

[<Fact>]
let ``parse project file JSON`` () = 
    let json = """
    {
      "version": 3,
      "targets": {
        ".NETCoreApp,Version=v2.0": {
          "FSharp.Compiler.Service/16.0.2": {
            "type": "package",
            "compile": {
              "lib/netstandard1.6/FSharp.Compiler.Service.dll": {}
            }
          }
        }
      },
      "libraries": {
        "FSharp.Compiler.Service/16.0.2": {
          "path": "fsharp.compiler.service/16.0.2"
        }
      },
      "packageFolders": {
        "/Users/george/.nuget/packages/": {},
        "/usr/local/share/dotnet/sdk/NuGetFallbackFolder": {}
      }
    }"""
    let parsed = ProjectManagerUtils.parseAssetsJson json
    Assert.True(Map.containsKey ".NETCoreApp,Version=v2.0" parsed.targets)
    Assert.True(Map.containsKey "FSharp.Compiler.Service/16.0.2" parsed.libraries)
    Assert.True(Seq.exists ((=) "/Users/george/.nuget/packages/") parsed.packageFolders)

[<Fact>]
let ``parse a project file`` () = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = ProjectManagerUtils.parseBoth file
    let hasName (name: string) (f: FileInfo) = name = f.Name
    Assert.True(Seq.exists (hasName "ProjectManager.fs") parsed.sources)
    Assert.True(Seq.exists (hasName "LSP.fsproj") parsed.projectReferences)
    Assert.True(Seq.exists (hasName "FSharp.Compiler.Service.dll") parsed.references)

let private fileHasName (name: string) (f: string) =
    let parts = f.Split('/')
    let fName = parts.[parts.Length - 1]
    name = fName 

let private projectHasName (name: string) (project: string * FSharpProjectOptions) = 
  let (f, _) = project
  let parts = f.Split('/')
  let fName = parts.[parts.Length - 1]
  name = fName

[<Fact>]
let ``parse a project file recursively`` () = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = ProjectManagerUtils.parseProjectOptions file
    Assert.True(Seq.exists (fileHasName "ProjectManager.fs") parsed.SourceFiles)
    Assert.True(Seq.exists (projectHasName "LSP.fsproj") parsed.ReferencedProjects)
    // Assert.True(Seq.exists (hasName "FSharp.Compiler.Service.dll") parsed.references)

[<Fact>]
let ``find an fsproj in a parent dir`` () = 
    let projects = ProjectManager()
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Program.fs"|])
    let parsed = projects.FindProjectOptions(Uri(file.FullName)) |> Option.get
    Assert.True(Seq.exists (fileHasName "ProjectManager.fs") parsed.SourceFiles)
    Assert.True(Seq.exists (projectHasName "LSP.fsproj") parsed.ReferencedProjects)
    // Assert.True(Seq.exists (hasName "FSharp.Compiler.Service.dll") parsed.references)