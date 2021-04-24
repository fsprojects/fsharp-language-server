module ProjectCrackerTests

open ProjectCracker
open ProjectCrackerTestsCommon
open LSP.Log
open System
open System.IO
open System.Text.RegularExpressions
open System.Diagnostics
open Microsoft.Build
open Microsoft.Build.Evaluation
open Microsoft.Build.Utilities
open Microsoft.Build.Framework
open Microsoft.Build.Logging
open Buildalyzer
open NUnit.Framework

[<SetUp>]
let setup() = 
    LSP.Log.diagnosticsLog := stdout

let containsFileName(name: string, files: FileInfo list) = 
  let test(f: FileInfo) = name = f.Name
  List.exists test files


[<Test>]
let ``crack a project file``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "MainProject.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    // Direct project reference
    let projectNames = [for f in cracked.projectReferences do yield f.Name]
    if not(List.contains "DependsOn.fsproj" projectNames) then
        Assert.Fail(sprintf "No DependsOn.fsproj in %A" cracked.projectReferences)
    // Transitive dependency
    if not(List.contains "IndirectDep.fsproj" projectNames) then
        Assert.Fail(sprintf "No IndirectDep.fsproj in %A" cracked.projectReferences)
    // Output dll
    Assert.AreEqual("MainProject.dll", cracked.target.Name)

[<Test>]
let ``crack a project file with case insensitive package references`` () =
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "HasPackageReference"; "HasPackageReference.fsproj" |] |> FileInfo
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.Contains([for f in cracked.packageReferences do yield f.Name], "Logary.dll")

[<Test>]
let ``find compile sources``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "IndirectDep"; "IndirectDep.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.AreEquivalent(["IndirectLibrary.fs"], [for f in cracked.sources do yield f.Name])

[<Test>]
let ``find reference includes``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"; "HasLocalDll.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.AreEquivalent(["IndirectDep.dll"], [for f in cracked.directReferences do yield f.Name])

[<Test>]
let ``find CSharp reference``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "ReferenceCSharp"; "ReferenceCSharp.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.AreEquivalent(["CSharpProject.dll"], [for f in cracked.otherProjectReferences do yield f.Name])

[<Test>]
let ``find CSharp reference with modified AssemblyName``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "ReferenceCSharp.AssemblyName"; "ReferenceCSharp.AssemblyName.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.AreEquivalent(["CSharpProject.AssemblyName.Modified.dll"], [for f in cracked.otherProjectReferences do yield f.Name])

[<Test>]
let ``resolve template params``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "TemplateParams"; "TemplateParams.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    let expected = [
        Path.Combine([|projectRoot.FullName; "src"; "fsharp"; "QueueList.fs"|]); 
        Path.Combine([|projectRoot.FullName; "sample"; "TemplateParams"; "netstandard2.0"; "pars.fs"|])
    ]
    let actual = [for f in cracked.sources do yield f.FullName]
    CollectionAssert.AreEquivalent(expected, actual)

// Check that project.assets.json-based ProjectCracker finds same .dlls as MSBuild

let clean(fsproj: FileInfo) = 
    let args = sprintf "clean %s" fsproj.FullName
    let info =
        ProcessStartInfo(
            UseShellExecute = false,
            FileName = "dotnet",
            Arguments = args
        )
    let p = Process.Start(info)
    p.WaitForExit()

let msbuild(fsproj: FileInfo): string list = 
    // Clean project so `dotnet build` actually generates output
    clean(fsproj)
    // Invoke `dotnet build`
    let args = sprintf "build %s -v d" fsproj.FullName
    let info =
        ProcessStartInfo(
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            FileName = "dotnet",
            Arguments = args
        )
    let p = Process.Start(info)
    // Collect all lines of stdout
    let lines = System.Collections.Generic.List<string>()
    p.OutputDataReceived.Add(fun args -> if args.Data <> null then lines.Add(args.Data))
    p.ErrorDataReceived.Add(fun args -> if args.Data <> null then dprintfn "Build: %A" args.Data)
    if not(p.Start()) then 
        failwithf "Failed dotnet %s" args
    p.BeginOutputReadLine()
    p.BeginErrorReadLine()
    p.WaitForExit()
    // Search for lines that start with '-r:'
    let references = System.Collections.Generic.List<string>()
    for line in lines do 
        if line.EndsWith("Task \"Fsc\"") then 
            references.Clear()
        if line.Trim().StartsWith("-r:") then 
            references.Add(line.Trim().Substring("-r:".Length))
    // Filter out project-to-project references, these are handled separately by ProjectCracker
    [ for r in references do 
        if not(r.Contains("bin/Debug/netcoreapp")) then 
            yield r ]
    
let cracker(fsproj: FileInfo): string list = 
    let cracked = ProjectCracker.crack(fsproj)
    [ for f in cracked.packageReferences do 
        yield f.FullName ]
        
[<Test>]
let ``find package references in EmptyProject``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "EmptyProject"; "EmptyProject.fsproj"|] |> FileInfo 
    CollectionAssert.AreEquivalent(msbuild(fsproj), cracker(fsproj))
        
[<Test>]
let ``find package references in FSharpKoans``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "FSharpKoans.Core"; "FSharpKoans.Core.fsproj"|] |> FileInfo 
    CollectionAssert.AreEquivalent(msbuild(fsproj), cracker(fsproj))
        
[<Test>]
let ``issue 28``() = 
    // NETStandard.Library is autoReferenced=true, but it is also an indirect dependency of dependencies that are not autoReferenced
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "Issue28"; "Issue28.fsproj"|] |> FileInfo 
    CollectionAssert.AreEquivalent(msbuild(fsproj), cracker(fsproj))

[<Test>]
let ``build unbuilt project``() = 
    let bin = Path.Combine [|projectRoot.FullName; "sample"; "NotBuilt"; "bin"|] 
    let obj = Path.Combine [|projectRoot.FullName; "sample"; "NotBuilt"; "obj"|] 
    if Directory.Exists(bin) then Directory.Delete(bin, true)
    if Directory.Exists(obj) then Directory.Delete(obj, true)
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "NotBuilt"; "NotBuilt.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    if cracked.error.IsSome then Assert.Fail(cracked.error.Value)
    CollectionAssert.AreEquivalent(["NotBuilt.fs"], [for f in cracked.sources do yield f.Name])
    CollectionAssert.IsNotEmpty(cracked.packageReferences)

[<Test>]
let ``find implicit references with netcoreapp3``() =
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "NetCoreApp3"; "NetCoreApp3.fsproj"|] |> FileInfo
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.Contains([for f in cracked.packageReferences do yield f.Name], "System.Core.dll")

[<Test>]
let ``find implicit references with net5``() =
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "Net5Console"; "Net5Console.fsproj"|] |> FileInfo
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.Contains([for f in cracked.packageReferences do yield f.Name], "System.Core.dll")
