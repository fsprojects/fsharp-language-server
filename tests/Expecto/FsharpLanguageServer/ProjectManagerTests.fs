module FSharpLanguageServer.Tests.ProjectManagerTests

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharpLanguageServer.Tests.Common
open FSharpLanguageServer
open System
open System.IO
open LSP.Types 
open FSharp.Data
open FSharp.Compiler.CodeAnalysis
open Expecto

type MockClient() = 
    member val Diagnostics = System.Collections.Generic.List<PublishDiagnosticsParams>()
    interface ILanguageClient with 
        member this.PublishDiagnostics(p: PublishDiagnosticsParams): unit = 
            ()
        member this.ShowMessage(p: ShowMessageParams): unit = 
            ()
        member this.RegisterCapability(p: RegisterCapability): unit = 
            ()
        member this.CustomNotification(method: string, p: JsonValue): unit = 
            ()

//setup

LSP.Log.diagnosticsLog := stdout
[<Tests>]
let tests=testList "formatting" [
     
    test "find project file" {
        let projects = ProjectManager(FSharpChecker.Create())
        let root = Path.Combine [|projectRoot.FullName; "sample"; "MainProject"|] |> DirectoryInfo
        Async.RunSynchronously(projects.AddWorkspaceRoot(root))
        let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
        let project = projects.FindProjectOptions(file)
        match project with 
        | Error(m) ->  failtestf "%A" m
        | Ok(f) -> if not(f.ProjectFileName.EndsWith "MainProject.fsproj") then  failtestf "%A" f
    }
     
    test "choose fsproj referenced by sln" {
        let projects = ProjectManager(FSharpChecker.Create())
        let root = Path.Combine [|projectRoot.FullName; "sample"; "SlnReferences"|] |> DirectoryInfo
        Async.RunSynchronously(projects.AddWorkspaceRoot(root))
        let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "SlnReferences"; "Main.fs"|])
        let project = projects.FindProjectOptions(file)
        match project with 
        | Error(m) ->  failtestf "%A" m
        | Ok(f) -> if not(f.ProjectFileName.EndsWith "ReferencedProject.fsproj") then  failtestf "%A" f
    }
     
    test "find script file" {
        let projects = ProjectManager(FSharpChecker.Create())
        let root = Path.Combine [|projectRoot.FullName; "sample"; "Script"|] |> DirectoryInfo
        Async.RunSynchronously(projects.AddWorkspaceRoot(root))
        let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "Script"; "LoadedByScript.fs"|])
        let project = projects.FindProjectOptions(file)
        match project with 
        | Error(m) ->  failtestf "%A" m
        | Ok(f) -> if not(f.ProjectFileName.EndsWith("MainScript.fsx.fsproj")) then  failtestf "%A" f
    }
     
    test "find an local dll" {
        let projects = ProjectManager(FSharpChecker.Create())
        let root = Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"|] |> DirectoryInfo
        Async.RunSynchronously(projects.AddWorkspaceRoot(root))
        let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "HasLocalDll"; "Program.fs"|])
        match projects.FindProjectOptions(file) with 
        | Error(m) ->  failtestf "%A" m
        | Ok(parsed) ->
            let isLocalDll(s: string) = s.EndsWith("IndirectDep.dll")
            if not (Seq.exists isLocalDll parsed.OtherOptions) then  failtestf "%A" parsed.OtherOptions
    }
     
    test "project-file-not-found" {
        let projects = ProjectManager(FSharpChecker.Create())
        let file = FileInfo(Path.Combine [|projectRoot.FullName; "sample"; "MainProject"; "Hover.fs"|])
        let project = projects.FindProjectOptions file
        match project with 
        | Ok(f) ->  failtestf "Shouldn't have found project file %s" f.ProjectFileName
        | Error(_) -> ()
    }
     
    test "bad project file" {
        let projects = ProjectManager(FSharpChecker.Create())
        let root = Path.Combine [|projectRoot.FullName; "sample"; "BadProject"|] |> DirectoryInfo
        Async.RunSynchronously(projects.AddWorkspaceRoot root)
    }
     
    test "get script options" {
        let projects = ProjectManager(FSharpChecker.Create())
        let script = Path.Combine [|projectRoot.FullName; "sample"; "Script"; "MainScript.fsx"|] |> FileInfo 
        projects.NewProjectFile(script)
        match projects.FindProjectOptions(script) with 
        | Error(m) ->  failtestf "%A" m
        | Ok(options) -> 
            let references = [for o in options.OtherOptions do if o.StartsWith("-r:") then yield FileInfo(o.Substring("-r:".Length)).Name]
            Expect.contains references "FSharp.Core.dll" "Missing ref"
            Expect.contains references "System.Runtime.dll" "Missing ref"
    }
]