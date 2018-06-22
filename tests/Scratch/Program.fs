module ProjectCrackerMain

open System
open System.IO
open System.Collections.Generic
open Microsoft.Build
open Microsoft.Build.Evaluation
open Microsoft.Build.Framework
open Microsoft.Build.Logging

[<EntryPoint>]
let main(argv: array<string>): int = 
    let basePath = "/usr/local/share/dotnet/sdk/2.1.300"
    Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(basePath, "Sdks"))
    Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(basePath, "MSBuild.dll"))
    let globalProperties = System.Collections.Generic.Dictionary<string, string>()
    globalProperties.Add("DesignTimeBuild", "true")
    globalProperties.Add("BuildingInsideVisualStudio", "true")
    globalProperties.Add("BuildProjectReferences", "false")
    globalProperties.Add("_ResolveReferenceDependencies", "true")
    globalProperties.Add("SolutionDir", "/Users/georgefraser/Documents/fsharp-language-server/sample")
    // Setting this property will cause any XAML markup compiler tasks to run in the
    // current AppDomain, rather than creating a new one. This is important because
    // our AppDomain.AssemblyResolve handler for MSBuild will not be connected to
    // the XAML markup compiler's AppDomain, causing the task not to be able to find
    // MSBuild.
    globalProperties.Add("AlwaysCompileMarkupFilesInSeparateDomain", "false")
    // This properties allow the design-time build to handle the Compile target without actually invoking the compiler.
    // See https://github.com/dotnet/roslyn/pull/4604 for details.
    globalProperties.Add("ProvideCommandLineArgs", "true")
    globalProperties.Add("SkipCompilerExecution", "true" )
    let projectCollection = new ProjectCollection(globalProperties)
    let project = projectCollection.LoadProject("/Users/georgefraser/Documents/fsharp-language-server/sample/ReferenceCSharp/ReferenceCSharp.fsproj")
    let instance = project.CreateProjectInstance()
    instance.Build([|"Compile"; "CoreCompile"|], [|ConsoleLogger() :> ILogger|]) |> ignore
    // Dump all items
    let mutable t = "" 
    for i in instance.Items do 
        if i.ItemType <> t then 
            printfn "%s" i.ItemType 
            t <- i.ItemType 
        printfn "  %A" i.EvaluatedInclude
    0
