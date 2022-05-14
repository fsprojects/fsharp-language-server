module ProjInfo

open Ionide.ProjInfo
open Ionide.ProjInfo.Types
open System.IO
open System
open FSharp.Compiler.CodeAnalysis
open System.Diagnostics
open Microsoft.Build.Utilities
let dotnetRestore(proj)=
        let args = sprintf "restore %s " proj
        let info =
            ProcessStartInfo(
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                FileName = "dotnet",
                Arguments = args
            )
        Process.Start(info).WaitForExit()
let crack path=

    dotnetRestore(path)
    let toolsPath = Init.init (DirectoryInfo Environment.CurrentDirectory) None
    let loader= Ionide.ProjInfo.WorkspaceLoader.Create(toolsPath)
    let mutable pos = Map.empty
    
    
    let errors=ResizeArray()
    loader.Notifications.Add (function
        | WorkspaceProjectState.Loaded (po, knownProjects, _) -> pos <- Map.add po.ProjectFileName po pos
        | WorkspaceProjectState.Failed(path,a)->
            errors.Add(a)
        |_->())
    let parsed = loader.LoadProjects [ path ] |> Seq.toList
    printfn "Errors:\n %A" errors 
    (parsed,FCS.mapToFSharpProjectOptions parsed.Head parsed,errors)
    // For more information see https://aka.ms/fsharp-console-apps
printfn "Hello from F#"

let crackFileInf (fileInfo:FileInfo)=
    crack(fileInfo.FullName)
