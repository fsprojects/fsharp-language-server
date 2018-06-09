module ProjectCrackerMain

open System
open System.IO
open System.Collections.Generic
open Microsoft.Build
open Microsoft.Build.Evaluation
open Microsoft.Build.Utilities
open Microsoft.Build.Framework
open Microsoft.Build.Logging

[<EntryPoint>]
let main(argv: array<string>): int = 
    let projectPath = "/Users/georgefraser/Documents/fsharp-language-server/sample/MainProject/MainProject.fsproj"
    // let projectPath = "/Users/georgefraser/Documents/FSharp.Compiler.Service/fcs/FSharp.Compiler.Service/FSharp.Compiler.Service.fsproj"
    // let solutionDir = Path.GetDirectoryName(projectPath)
    // let toolsPath = " /Library/Frameworks/Mono.framework/Versions/5.10.1/lib/mono/msbuild/15.0/bin"
    let toolsPath = "/usr/local/share/dotnet/sdk/2.1.200"
    let msBuildExePath = Path.Combine(toolsPath, "MSBuild.dll")
    // let msBuildExePath = "/Users/georgefraser/Documents/fsharp-language-server/tests/Scratch/bin/Debug/netcoreapp2.0/MSBuild.dll"
    // let extensionsPath = toolsPath
    // let sdksPath = Path.Combine(toolsPath, "Sdks")
    // let roslynTargetsPath = Path.Combine(toolsPath, "Roslyn")
    // let globalProperties = Dictionary<string, string>()
    // globalProperties.Add("DesignTimeBuild", "true")
    // globalProperties.Add("BuildProjectReferences", "false")
    // globalProperties.Add("SkipCompilerExecution", "true")
    // globalProperties.Add("ProvideCommandLineArgs", "true")
    // globalProperties.Add("SolutionDir", solutionDir)
    // globalProperties.Add("MSBuildExtensionsPath", extensionsPath)
    // globalProperties.Add("MSBuildSDKsPath", sdksPath)
    // globalProperties.Add("RoslynTargetsPath", roslynTargetsPath)
    // Environment.SetEnvironmentVariable("MSBuildExtensionsPath", extensionsPath)
    // Environment.SetEnvironmentVariable("MSBuildSDKsPath", sdksPath)
    // Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", extensionsPath)
    // Environment.SetEnvironmentVariable("MSBuildExtensionsPath64", extensionsPath)
    // Environment.SetEnvironmentVariable("MSBuildSDKsPath", sdksPath)
    Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", msBuildExePath)
    let projectCollection = new ProjectCollection()
    // projectCollection.RemoveAllToolsets()
    // projectCollection.AddToolset(new Toolset(ToolLocationHelper.CurrentToolsVersion, toolsPath, projectCollection, ""))
    let project = projectCollection.LoadProject(projectPath)
    let instance = project.CreateProjectInstance()
    // instance.Build("Compile", [ConsoleLogger() :> ILogger]) |> ignore
    // Dump all items
    let mutable t = "" 
    for i in instance.Items do 
        if i.ItemType <> t then 
            printfn "%s" i.ItemType 
            t <- i.ItemType 
        printfn "  %A" i.EvaluatedInclude
    0

(*
// This is just a utility for invoking Buildalyzer with more detailed output

let private buildFcs() = 
    let fsproj = "/Users/georgefraser/Documents/FSharp.Compiler.Service/fcs/FSharp.Compiler.Service/FSharp.Compiler.Service.fsproj"
    let manager = new AnalyzerManager()
    let analyzer = manager.GetProject(fsproj)
    // Print available targets
    for KeyValue(key, value) in analyzer.Project.Targets do 
        printfn "%s: %s" key value.DependsOnTargets
    // Invoke Restore, then build
    let project = analyzer.Load()
    let compile = project.CreateProjectInstance()
    let logger = Microsoft.Build.Logging.ConsoleLogger() :> Microsoft.Build.Framework.ILogger
    compile.Build("Restore", [logger]) |> ignore
    compile.Build("Build", [logger]) |> ignore
    // Dump all items
    let mutable t = "" 
    for i in compile.Items do 
        if i.ItemType <> t then 
            printfn "%s" i.ItemType 
            t <- i.ItemType 
        printfn "  %A" i

// Mono /Library/Frameworks/Mono.framework/Versions/5.10.1/lib/mono/msbuild/15.0/bin/Sdks/Microsoft.NET.Sdk/tools/net46/Microsoft.NET.Build.Tasks.dll
// Dotnet /usr/local/share/dotnet/sdk/2.1.105/Sdks/Microsoft.NET.Sdk/tools/netcoreapp1.0/Microsoft.NET.Build.Tasks.dll

//   MSBuildExtensionsPath = /usr/local/share/dotnet/sdk/2.1.200/
//   MSBuildSDKsPath = /usr/local/share/dotnet/sdk/2.1.200/Sdks
//   RoslynTargetsPath = /usr/local/share/dotnet/sdk/2.1.200/Roslyn

let private buildSomethingWellBehaved() = 
    let fsproj = "/Users/georgefraser/Documents/FSharp.Compiler.Service/fcs/FSharp.Compiler.Service/FSharp.Compiler.Service.fsproj"
    let options = new AnalyzerManagerOptions()
    options.LogWriter <- Console.Error
    options.CleanBeforeCompile <- false
    let manager = new AnalyzerManager(options)
    let analyzer = manager.GetProject(fsproj)
    // Implicitly does analyzer.Load().CreateProjectInstance(), builds the "Compile" target
    let compile = analyzer.Compile()
    // Dump all items
    let mutable t = "" 
    for i in compile.Items do 
        if i.ItemType <> t then 
            printfn "%s" i.ItemType 
            t <- i.ItemType 
        printfn "  %A" i.EvaluatedInclude
*)