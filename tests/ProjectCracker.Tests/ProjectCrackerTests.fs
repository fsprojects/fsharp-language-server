module ProjectCrackerTests

open ProjectCracker
open ProjectCrackerTestsCommon
open LSP.Log
open System
open System.IO
open System.Text.RegularExpressions
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
let ``find compile sources``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "IndirectDep"; "IndirectDep.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    CollectionAssert.AreEquivalent(["IndirectLibrary.fs"], [for f in cracked.sources do yield f.Name])

[<Test>]
let ``crack script defaults``() = 
    let cracked = ProjectCracker.scriptBase.Value 
    let packages = [for f in cracked.packageReferences do yield f.Name]
    if not(List.contains "FSharp.Core.dll" packages) then 
        Assert.Fail(sprintf "No FSharp.Core.dll in %A" cracked.packageReferences)

// Check that project.assets.json-based ProjectCracker finds same .dlls as MSBuild

let msbuild(fsproj: FileInfo): string list = 
    // Create an msbuild instance
    let options = new AnalyzerManagerOptions()
    options.LogWriter <- !diagnosticsLog
    let manager = new AnalyzerManager(options)
    // Compile the project
    let analyzer = manager.GetProject(fsproj.FullName)
    let compile = analyzer.Compile()
    // Get package references from build
    let packageReferences = compile.GetItems("ReferencePath")
    [ for i in packageReferences do 
        let relativePath = i.EvaluatedInclude
        // Exclude project references
        if not(relativePath.Contains("bin/Debug/netcoreapp2.0")) then
            let absolutePath = Path.Combine(fsproj.DirectoryName, relativePath)
            yield Path.GetFullPath(absolutePath) ]
    
let cracker(fsproj: FileInfo): string list = 
    let cracked = ProjectCracker.crack(fsproj)
    [ for f in cracked.packageReferences do 
        yield f.FullName ]
        
[<Test>]
let ``find package references in FSharpLanguageServer``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "src"; "FSharpLanguageServer"; "FSharpLanguageServer.fsproj"|] |> FileInfo 
    CollectionAssert.AreEquivalent(msbuild(fsproj), cracker(fsproj))
        
[<Test>]
let ``find package references in FSharpKoans``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "FSharpKoans.Core"; "FSharpKoans.Core.fsproj"|] |> FileInfo 
    CollectionAssert.AreEquivalent(msbuild(fsproj), cracker(fsproj))

[<Test>]
let ``error for unbuilt project``() = 
    let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "NotBuilt"; "NotBuilt.fsproj"|] |> FileInfo 
    let cracked = ProjectCracker.crack(fsproj)
    match cracked.error with 
    | None -> Assert.Fail("Should have failed to crack unbuilt project")
    | Some(e) -> StringAssert.Contains("project.assets.json does not exist", e)