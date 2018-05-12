module Main.Tests.ProjectParserTests

open Main
open Main.Tests.Common
open System
open System.IO
open Microsoft.FSharp.Compiler.SourceCodeServices
open SimpleTest

let ``test parsing a JSON project file`` (t: TestContext) = 
    let json = """
    {
      "version": 3,
      "targets": {
        ".NETCoreApp,Version=v2.0": {
          "FSharp.Compiler.Service/22.0.3": {
            "type": "package",
            "compile": {
              "lib/netstandard1.6/FSharp.Compiler.Service.dll": {}
            }
          }
        }
      },
      "libraries": {
        "FSharp.Compiler.Service/22.0.3": {
          "type": "package",
          "path": "fsharp.compiler.service/22.0.3",
          "files": [
            "fsharp.compiler.service.22.0.3.nupkg.sha512",
            "fsharp.compiler.service.nuspec",
            "lib/net45/FSharp.Compiler.Service.dll",
            "lib/net45/FSharp.Compiler.Service.xml",
            "lib/netstandard2.0/FSharp.Compiler.Service.dll",
            "lib/netstandard2.0/FSharp.Compiler.Service.xml"
          ]
        }
      },
      "packageFolders": {
        "/Users/george/.nuget/packages/": {},
        "/usr/local/share/dotnet/sdk/NuGetFallbackFolder": {}
      },
      "project": {
        "version": "1.0.0",
        "frameworks": {
          "netcoreapp2.0": {
            "dependencies": {
              "FSharp.Compiler.Service": {
                "target": "Package",
                "version": "[22.0.3, )"
              }
            }
          }
        }
      }
    }"""
    let parsed = ProjectParser.parseAssetsJson json
    if not (Map.containsKey "FSharp.Compiler.Service/22.0.3" parsed.libraries) then Fail(sprintf "%A" parsed.libraries)
    let packageFolders = parsed.packageFolders |> Map.toSeq |> Seq.map fst |> List.ofSeq
    if not (Seq.exists ((=) "/Users/george/.nuget/packages/") (packageFolders)) then Fail(sprintf "%A" parsed.packageFolders)

let ``test parsing a project file`` (t: TestContext) = 
    let file = FileInfo(Path.Combine [|projectRoot.FullName; "src"; "Main"; "Main.fsproj"|])
    let parsed = match ProjectParser.parseFsProj file with Ok p -> p
    let hasName (name: string) (f: FileInfo) = f.Name = name
    if not (Seq.exists (hasName "ProjectManager.fs") parsed.compileInclude) then 
        Fail(sprintf "No ProjectManager.fs in %A" parsed.compileInclude)
    if not (Seq.exists (hasName "LSP.fsproj") parsed.projectReferenceInclude) then 
        Fail(sprintf "No LSP.fsproj in %A" parsed.projectReferenceInclude)

let ``test substitute parameters in a project file`` (t: TestContext) = 
  let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "TemplateParams"; "TemplateParams.fsproj"|] |> FileInfo 
  let parse = match ProjectParser.parseFsProj fsproj with Ok p -> p
  let expectedFile = Path.Combine [|projectRoot.FullName; "src"; "fsharp"; "QueueList.fs"|] |> FileInfo 
  if parse.compileInclude |> List.map (fun f -> f.FullName) <> [expectedFile.FullName] then Fail(parse.compileInclude)