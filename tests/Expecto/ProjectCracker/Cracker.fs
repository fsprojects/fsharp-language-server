module ProjectCrackerTests
open LSP.Utils
open ProjInfo
open LSP.Log
open System
open System.IO
open System.Diagnostics

open Expecto


let private findProjectRoot (start: DirectoryInfo): DirectoryInfo = 
    seq {
        let mutable dir = start 
        while dir <> dir.Root do 
            for _ in dir.GetFiles "fsharp-language-server.sln" do 
                yield dir
            dir <- dir.Parent
    } |> Seq.head
let private testDirectory = DirectoryInfo(Directory.GetCurrentDirectory())
let projectRoot = findProjectRoot testDirectory


let crackingTests  =        
    testList "cracking test" [
    test "crack a project file with projinfo"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "MainProject.fsproj"|] |> normedFileInfo 
        let (cracked,opts,errs) = ProjInfo.crack(fsproj.FullName)
        printfn "ERROR:\n %A" errs
        Expect.isNonEmpty cracked "Should have at least one project"
        // Direct project reference
        let projectNames = [for f in cracked do yield f.ProjectFileName|>Path.GetFileName]
        if not(List.contains "DependsOn.fsproj" projectNames) then
            failtestf "No DependsOn.fsproj in %A" cracked.Head.ReferencedProjects
        // Transitive dependency
        if not(List.contains "IndirectDep.fsproj" projectNames) then
            failtestf "No IndirectDep.fsproj in %A" cracked.Head.ReferencedProjects
        // Output dll
        
        
    }    
    test "crack a project file"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "MainProject.fsproj"|] |> normedFileInfo 
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        // Direct project reference
        let projectNames = crack.Head.ReferencedProjects|>List.map (fun x-> x.ProjectFileName|>Path.GetFileName)
        if not(List.contains "DependsOn.fsproj" projectNames) then
            failtestf "No DependsOn.fsproj in %A" opts.ReferencedProjects
        // Transitive dependency
        if not(List.contains "IndirectDep.fsproj" projectNames) then
            failtestf "No IndirectDep.fsproj in %A" opts.ReferencedProjects
        // Output dll
        Expect.equal "MainProject.dll" (crack.Head.TargetPath|>Path.GetFileName) "not equal"
    }

    test "crack a project file with case insensitive package references"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "HasPackageReference"; "HasPackageReference.fsproj" |] |> FileInfo
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        Expect.contains [for f in crack.Head.PackageReferences do yield f.FullPath|>Path.GetFileName] "Logary.dll" "missing ref"
    }
    
    test "find compile sources"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "IndirectDep"; "IndirectDep.fsproj"|] |> normedFileInfo 
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        Expect.containsAll  [for f in cracked.SourceFiles do yield f|>Path.GetFileName] ["IndirectLibrary.fs"] "sequences don't match"
    }
//Not sure if this is a problem. We do get intellisense for the types in the dll
    ptest "find reference includes"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"; "HasLocalDll.fsproj"|] |> normedFileInfo 
        let ( crack,opts,e) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        
        Expect.containsAll [for f in opts.ReferencedProjects do yield f.FileName|>Path.GetFileName]  ["IndirectDep.dll"] "sequences don't match"
    }

    test "find CSharp reference"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "ReferenceCSharp"; "ReferenceCSharp.fsproj"|] |> normedFileInfo 
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        Expect.containsAll  [for f in cracked.ReferencedProjects do yield f.ProjectFileName|>Path.GetFileName] ["CSharpProject.csproj"] "sequences don't match"
    }

    test "find CSharp reference with modified AssemblyName"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "ReferenceCSharp.AssemblyName"; "ReferenceCSharp.AssemblyName.fsproj"|] |> normedFileInfo 
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        let projects=cracked.ReferencedProjects|>List.map( fun ref->crack|>List.find(fun x->ref.ProjectFileName=x.ProjectFileName))
        Expect.sequenceEqual  [for f in projects  do yield f.TargetPath|>Path.GetFileName] ["CSharpProject.AssemblyName.Modified.dll"] "sequences don't match"
    }
//If this becomes a problem we can deal with it later. for now i'm not going to worry
    ptest "resolve template params"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "TemplateParams"; "TemplateParams.fsproj"|] |> normedFileInfo 
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        let expected = [
            Path.Combine([|projectRoot.FullName; "src"; "fsharp"; "QueueList.fs"|]); 
            Path.Combine([|projectRoot.FullName; "sample"; "TemplateParams"; "netstandard2.0"; "pars.fs"|])
        ]
        let actual = [for f in cracked.SourceFiles do yield f]
        Expect.sequenceEqual expected actual "sequences don't match"
    } 
    ]
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
    let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
    [ for f in crack.Head.PackageReferences do 
        yield f.FullPath ]
        

let tests2  =        
    testSequenced <| testList "Parser2 test" [
    test "find package references in EmptyProject"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "EmptyProject"; "EmptyProject.fsproj"|] |> normedFileInfo 
        Expect.containsAll (msbuild(fsproj)) (cracker(fsproj)) "sequences don't match"
            
    }
    test "find package references in FSharpKoans"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "FSharpKoans.Core"; "FSharpKoans.Core.fsproj"|] |> normedFileInfo 
        Expect.containsAll (msbuild(fsproj)) (cracker(fsproj)) "sequences don't match"
            
    }
    test "issue 28"{
        // NETStandard.Library is autoReferenced=true, but it is also an indirect dependency of dependencies that are not autoReferenced
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "Issue28"; "Issue28.fsproj"|] |> normedFileInfo 
        Expect.containsAll (msbuild(fsproj)) (cracker(fsproj)) "sequences don't match"
    }

    test "build unbuilt project"{
        let bin = Path.Combine [|projectRoot.FullName; "sample"; "NotBuilt"; "bin"|] 
        let obj = Path.Combine [|projectRoot.FullName; "sample"; "NotBuilt"; "obj"|] 
        if Directory.Exists(bin) then Directory.Delete(bin, true)
        if Directory.Exists(obj) then Directory.Delete(obj, true)
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "NotBuilt"; "NotBuilt.fsproj"|] |> normedFileInfo 
        let ( crack,opts,error) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        if error.Count>0 then failtestf " Contained erros %A" (error)
        Expect.containsAll  [for f in cracked.SourceFiles do yield f|>Path.GetFileName] ["NotBuilt.fs"] "sequences don't match"
        Expect.isNonEmpty(cracked.PackageReferences) "has references"
    }
(* 
    test "find implicit references with netcoreapp3"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "NetCoreApp3"; "NetCoreApp3.fsproj"|] |> FileInfo
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        Expect.contains [for f in cracked.packageReferences do yield f.Name] "System.Core.dll" "missing ref"
    }

    test "find implicit references with net5"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "Net5Console"; "Net5Console.fsproj"|] |> FileInfo
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        Expect.contains [for f in cracked. do yield f.Name] "System.Core.dll" "missing ref"
    }
    test "find implicit references with net6"{
        let fsproj = Path.Combine [|projectRoot.FullName; "sample"; "Net6Console"; "Net6Console.fsproj"|] |> FileInfo
        let ( crack,opts,_) = ProjInfo.crackFileInf(fsproj)
        let cracked=crack|>List.head
        Expect.contains [for f in cracked.packageReferences do yield f.Name] "System.Core.dll" "missing ref"
    }  *)
]  