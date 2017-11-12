module Main.ProjectManagerTests

open System
open System.IO
open NUnit.Framework

let findProjectRoot (start: DirectoryInfo): DirectoryInfo = 
    seq {
        let mutable dir = start 
        while dir <> dir.Root do 
            for _ in dir.GetFiles "fsharp-language-server.sln" do 
                yield dir
            dir <- dir.Parent
    } |> Seq.head
let testDirectory = DirectoryInfo(TestContext.CurrentContext.TestDirectory)
let projectRoot = findProjectRoot testDirectory

[<Test>]
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
    Assert.That(parsed.packageFolders, Contains.Item "/Users/george/.nuget/packages/")

[<Test>]
let ``parse a project file`` () = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = ProjectManagerUtils.parseBoth file
    let name (f: FileInfo): string = f.Name
    Assert.That(parsed.sources |> Seq.map name, Contains.Item "ProjectManager.fs")
    Assert.That(parsed.projectReferences |> Seq.map name, Contains.Item "LSP.fsproj")
    Assert.That(parsed.references |> Seq.map name, Contains.Item "FSharp.Compiler.Service.dll")