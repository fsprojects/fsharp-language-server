module ProjectCracker

open LSP.Log
open System
open System.IO
open Buildalyzer
open System.Xml
open FSharp.Data
open LSP.Json.Ser
open Microsoft.Build.Framework
open Microsoft.Build.Logging
open Microsoft.Build.Execution

// TODO investigate how MS solves this problem:
// Omnisharp-roslyn cracks .csproj files: https://github.com/OmniSharp/omnisharp-roslyn/blob/master/tests/OmniSharp.MSBuild.Tests/ProjectFileInfoTests.cs
// Roslyn cracks .csproj files: https://github.com/dotnet/roslyn/blob/master/src/Workspaces/MSBuildTest/MSBuildWorkspaceTests.cs

type CrackedProject = {
    /// ?.fsproj file that was cracked
    fsproj: FileInfo 
    /// ?.dll file built by this .fsproj file
    /// Dependent projects will reference this dll in fscArgs, like "-r:?.dll"
    target: FileInfo
    /// List of source files.
    /// These are fsc args, but presented separately because that's how FSharpProjectOptions wants them.
    sources: FileInfo list
    /// .fsproj files on references projects 
    projectReferences: FileInfo list 
    /// .dlls
    packageReferences: FileInfo list 
}

let private files(fsproj: FileInfo, items: ProjectItemInstance seq) = 
    [ for i in items do 
        let relativePath = i.EvaluatedInclude
        let absolutePath = Path.Combine(fsproj.DirectoryName, relativePath)
        let normalizePath = Path.GetFullPath(absolutePath)
        yield FileInfo(normalizePath) ]

/// Crack an .fsproj file by running the "Compile" target and asking MSBuild what to do
let crack(fsproj: FileInfo): Result<CrackedProject, string> = 
    // Create an msbuild instance
    let options = new AnalyzerManagerOptions()
    options.LogWriter <- !diagnosticsLog // TODO this doesn't follow ref changes
    options.CleanBeforeCompile <- false
    let manager = new AnalyzerManager(options) // TODO consider keeping this alive between invocations
    // Compile the project
    try 
        let analyzer = manager.GetProject(fsproj.FullName)
        let compile = analyzer.Compile()
        // Get key items from build output
        // TODO consider using checker.GetProjectOptionsFromCommandLineArgs
        let targets = compile.GetItems("IntermediateAssembly")
        let sources = compile.GetItems("Compile")
        let projectReferences = compile.GetItems("ProjectReference")
        let packageReferences = compile.GetItems("ReferencePath")
        let [target] = files(fsproj, targets) // TODO this might not be true in error cases
        Ok({
            fsproj=fsproj
            target=target
            sources=files(fsproj, sources)
            projectReferences=files(fsproj, projectReferences)
            packageReferences=files(fsproj, packageReferences)
        })
    with e -> Error(e.Message)

/// Get the baseline options for an .fsx script
/// In theory this should be done by FSharpChecker.GetProjectOptionsFromScript,
/// but it appears to be broken on dotnet core: https://github.com/fsharp/FSharp.Compiler.Service/issues/847
let scriptBase: Lazy<CrackedProject> = 
    lazy 
        match crack(FileInfo("/Users/georgefraser/Documents/fsharp-language-server/client/PseudoScript.fsproj")) with 
        | Ok(options) -> options 
        | Error(message) -> raise(Exception(sprintf "Failed to load PseudoScript.fsproj: %s" message))