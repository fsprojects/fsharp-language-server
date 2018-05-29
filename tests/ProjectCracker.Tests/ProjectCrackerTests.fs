module ProjectCrackerTests

open ProjectCracker
open ProjectCrackerTestsCommon
open System
open System.IO
open System.Text.RegularExpressions
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
    match ProjectCracker.crack(fsproj) with 
    | Error(e) -> Assert.Fail(e)
    | Ok(cracked) -> 
        // Direct project reference
        let projectNames = [for f in cracked.projectReferences do yield f.Name]
        if not(List.contains "DependsOn.fsproj" projectNames) then
            Assert.Fail(sprintf "No DependsOn.fsproj in %A" cracked.projectReferences)
        // Transitive dependency
        if not(List.contains "IndirectDep.fsproj" projectNames) then
            Assert.Fail(sprintf "No IndirectDep.fsproj in %A" cracked.projectReferences)
        // Output dll
        Assert.AreEqual("MainProject.dll", cracked.target.Name)