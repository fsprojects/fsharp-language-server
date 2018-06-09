module ProjectCracker

open LSP.Log
open System
open System.IO
open System.Xml
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Collections.Generic
open FSharp.Data
open FSharp.Data.JsonExtensions
open LSP.Json.Ser
open Microsoft.Build
open Microsoft.Build.Evaluation
open Microsoft.Build.Utilities
open Microsoft.Build.Framework
open Microsoft.Build.Logging

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

/// Read the assembly name and version from a .dll using System.Reflection.Metadata
let private readAssembly(dll: FileInfo): Result<string * Version, string> = 
    try 
        use fileStream = new FileStream(dll.FullName, FileMode.Open, FileAccess.Read)
        use peReader = new PEReader(fileStream, PEStreamOptions.LeaveOpen)
        let metadataReader = peReader.GetMetadataReader()
        let assemblyDefinition = metadataReader.GetAssemblyDefinition()
        Ok(metadataReader.GetString(assemblyDefinition.Name), assemblyDefinition.Version)
    with e -> Error(e.Message)

type private ProjectAssets = {
    framework: string 
    packages: FileInfo list 
    projects: FileInfo list
}

let private parseProjectAssets(projectAssetsJson: FileInfo): ProjectAssets = 
    dprintfn "Parsing %s" projectAssetsJson.FullName
    let root = JsonValue.Parse(File.ReadAllText(projectAssetsJson.FullName))
    let fsproj = FileInfo(root?project?restore?projectPath.AsString())  
    // Choose one of the frameworks listed in project.frameworks
    // by scanning all possible frameworks in order of preference
    let shortFramework, longFramework = 
        let preference = [
            "netcoreapp2.1", ".NETCoreApp,Version=v2.1";
            "netcoreapp2.0", ".NETCoreApp,Version=v2.0";
            "netcoreapp1.1", ".NETCoreApp,Version=v1.1";
            "netcoreapp1.0", ".NETCoreApp,Version=v1.0";

            "netstandard2.0", ".NETStandard,Version=v2.0";
            "netstandard1.6", ".NETStandard,Version=v1.6";
            "netstandard1.5", ".NETStandard,Version=v1.5";
            "netstandard1.4", ".NETStandard,Version=v1.4";
            "netstandard1.3", ".NETStandard,Version=v1.3";
            "netstandard1.2", ".NETStandard,Version=v1.2";
            "netstandard1.1", ".NETStandard,Version=v1.1";
            "netstandard1.0", ".NETStandard,Version=v1.0";

            "net472", ".NETFramework,Version=v4.7.2";
            "net471", ".NETFramework,Version=v4.7.1";
            "net47", ".NETFramework,Version=v4.7";
            "net462", ".NETFramework,Version=v4.6.2";
            "net461", ".NETFramework,Version=v4.6.1";
            "net46", ".NETFramework,Version=v4.6";
            "net452", ".NETFramework,Version=v4.5.2";
            "net451", ".NETFramework,Version=v4.5.1";
            "net45", ".NETFramework,Version=v4.5";
            "net403", ".NETFramework,Version=v4.0.3";
            "net40", ".NETFramework,Version=v4.0";
            "net35", ".NETFramework,Version=v3.5";
            "net20", ".NETFramework,Version=v2.0";
            "net11", ".NETFramework,Version=v1.1" ]
        let projectContainsFramework(short: string) = 
            root?project?frameworks.TryGetProperty(short).IsSome
        let mutable found: (string * string) option = None
        for short, long in preference do 
            if projectContainsFramework(short) && found.IsNone then
                found <- Some(short, long)
        found.Value
    dprintfn "Chose framework %s / %s" shortFramework longFramework
    // Find all transitive dependencies of the project 
    // by starting with `$.projects.frameworks[?].dependencies`
    // and recursively walking the map described by `$.targets`
    // ["FSharp.Core/4.3.4"; ...]
    let transitiveDependencies = 
        // Choose a version of a dependency by scanning targets
        let chooseVersion(dependencyName: string): string = 
            let mutable found: string option = None 
            for dependencyVersion, _ in root?targets.[longFramework].Properties do 
                if dependencyVersion.StartsWith(dependencyName + "/") && found.IsNone then 
                    found <- Some(dependencyVersion)
            found.Value
        // Get the direct dependencies of `dependencyName` by looking at `targets[dependencyName/version].dependencies`
        let nextDependencies(dependencyName: string) = 
            let version = chooseVersion(dependencyName) 
            match root?targets.[longFramework].[version].TryGetProperty("dependencies") with 
            | None -> []
            | Some(ds) -> [for d, _ in ds.Properties do yield d]
        // ["FSharp.Core"; ...]
        let packages = 
            [ for name, value in root?project?frameworks.[shortFramework]?dependencies.Properties do 
                if value?target.AsString() = "Package" then 
                    yield name ]
        let projects = 
            [ for name, value in root?libraries.Properties do 
                if value?``type``.AsString() = "project" then 
                    yield Array.head(name.Split('/')) ]
        // ["FSharp.Core"; ...]
        let transitiveDependencies = HashSet<string>()
        // Walk transitive dependencies of `d`, adding them to `transitiveDependencies`
        let rec walk(d: string) = 
            if transitiveDependencies.Add(d) then 
                for d in nextDependencies(d) do 
                    walk(d)
        // Start with the root dependencies
        for d in packages do 
            walk(d)
        for d in projects do 
            walk(d)
        // ["FSharp.Core/4.3.4"; ...]
        [for d in transitiveDependencies do yield chooseVersion(d)]
    dprintfn "Transitive dependencies are %A" transitiveDependencies
    // Look up each transitive dependency in $.libraries
    let libraries = 
        // Get info from $.libraries for each dependency
        [ for d in transitiveDependencies do
            match root?libraries.TryGetProperty(d) with 
            | None -> 
                dprintfn "%s does not exist in libraries" d
            | Some(lib) -> 
                if lib?``type``.AsString() = "package" then
                    yield d, lib ]
    // Find all package dlls
    let packageDlls = 
        // ["/Users/georgefraser/.nuget/packages/", ...]
        let packageFolders = [for p, _ in root?packageFolders.Properties do yield p]
        // Search package folders for a .dll
        let absoluteDll(relativeToPackageFolder: string): string option = 
            let mutable found: string option = None
            for packageFolder in packageFolders do 
                let candidate = Path.Combine(packageFolder, relativeToPackageFolder)
                if File.Exists(candidate) && found.IsNone then 
                    found <- Some(candidate)
            if found.IsNone then 
                dprintfn "Couldn't find %s in %A" relativeToPackageFolder packageFolders 
            found
        // "targets": {
        //     ".NETCoreApp,Version=v2.0": {
        //         "FSharp.Core/4.3.4": {
        //             "type": "package",
        //                 "compile": {
        //                     "lib/netstandard1.6/FSharp.Core.dll": {}
        //                 },
        //                 "runtime": {
        //                     "lib/netstandard1.6/FSharp.Core.dll": {}
        //                 },
        //                 "resource": { ... }
        //             }
        //         }
        //     }
        // }
        // "libraries": {
        //     "FSharp.Core/4.3.4": {
        //         "sha512": "u2UeaUl1pt/Lktdpzq3AsaRmOV1mOiQaSbZgYqQQYuqBSjnILWemetff4xMZIAZi0241jlIkcrJQsU5PlLwIJA==",
        //         "type": "package",
        //         "path": "fsharp.core/4.3.4",
        //         "files": [ ... ]
        //     },
        // }
        let findPackageDlls(dependencyWithVersion: string, library: JsonValue): string list = 
            let prefix = library?path.AsString()
            match root?targets.[longFramework].[dependencyWithVersion].TryGetProperty("compile") with 
            | None -> 
                dprintfn "%s has no compile-time dependencies" dependencyWithVersion
                []
            | Some(map) -> 
                [ for dll, _ in map.Properties do 
                    let rel = Path.Combine(prefix, dll)
                    match absoluteDll(rel) with 
                    | None -> ()
                    | Some(abs) -> yield abs ]
        List.collect findPackageDlls libraries
    // Resolve conflicts by getting name and version from each DLL,
    // choosing the highest version
    let packageDllsWithoutConflicts =
        let packageDllsWithName = 
            [ for d in packageDlls do 
                match readAssembly(FileInfo(d)) with 
                | Error(e) -> dprintfn "Failed loading %s with error %s" d e
                | Ok(name, version) -> yield d, name, version ]
        let aName(dll, name, version) = name
        let aVersion(dll, name, version) = version
        let byName = List.groupBy aName packageDllsWithName
        [ for name, versions in byName do 
            match versions with 
            | [(file, _, _)] -> yield file
            | _ -> 
                let winner, _, _ = List.maxBy aVersion versions
                dprintfn "Conflict between %A, chose %s" versions winner
                yield winner ]
    // Find all transitive project dependencies by examining libraries
    // Values with "type": "project" are projects
    // All transitive projects will already be included in project.assets.json 
    // "targets": {
    //     ".NETCoreApp,Version=v2.0": {
    //         "LSP/1.0.0": {
    //             "type": "project",
    //             "framework": ".NETCoreApp,Version=v2.0",
    //             "dependencies": { ... },
    //             "compile": {
    //                 "bin/placeholder/LSP.dll": {}
    //             },
    //             "runtime": {
    //                 "bin/placeholder/LSP.dll": {}
    //             }
    //         },
    //     }
    // }
    // "libraries": {
    //     "LSP/1.0.0": {
    //         "type": "project",
    //         "path": "../LSP/LSP.fsproj",
    //         "msbuildProject": "../LSP/LSP.fsproj"
    //     }
    // }
    let projects = 
        [ for name, library in root?libraries.Properties do 
            if library?``type``.AsString() = "project" then 
                let rel = library?path.AsString()
                let abs = Path.Combine(fsproj.DirectoryName, rel)
                let norm = Path.GetFullPath(abs)
                yield FileInfo(norm) ]
    {
        framework=shortFramework
        packages=List.map FileInfo packageDllsWithoutConflicts
        projects=projects
    }

type TempEnv(key: string, value: string) =
    let before = Environment.GetEnvironmentVariable(key)
    do Environment.SetEnvironmentVariable(key, value)
    interface IDisposable with 
        member this.Dispose() = 
            Environment.SetEnvironmentVariable(key, before)

// Run `msbuild restore` and return the msbuild API representation of the project
let private restore(fsproj: FileInfo): Execution.ProjectInstance = 
    // let projectCrackerDll = FileInfo(Assembly.GetExecutingAssembly().Location)
    // let toolsPath = projectCrackerDll.DirectoryName
    // let msBuildExePath = Path.Combine(toolsPath, "MSBuild.dll")
    let toolsPath = "/usr/local/share/dotnet/sdk/2.1.200" // TODO get this dynamically or use Buildalyzer
    let msBuildExePath = Path.Combine(toolsPath, "MSBuild.dll")
    if not(File.Exists(msBuildExePath)) then 
        raise(Exception(sprintf "%s does not exist" msBuildExePath))
    else 
        dprintfn "Using %s" msBuildExePath
    use env = new TempEnv("MSBUILD_EXE_PATH", msBuildExePath)
    let projectCollection = new ProjectCollection()
    let project = projectCollection.LoadProject(fsproj.FullName)
    let instance = project.CreateProjectInstance()
    let log = ConsoleLogger(LoggerVerbosity.Normal, (fun m -> dprintfn "%s" m), null, null)
    // TODO more optimizations from Buildalyzer / Scratch
    // instance.Build("Restore", [log :> ILogger]) |> ignore
    instance

/// Crack an .fsproj file by:
/// - Running the "Restore" target and reading 
/// - Reading .fsproj using the MSBuild API
/// - Reading libraries from project.assets.json
let crack(fsproj: FileInfo): Result<CrackedProject, string> = 
    try 
        // Get package info from project.assets.json
        let projectAssetsJson = FileInfo(Path.Combine [|fsproj.DirectoryName; "obj"; "project.assets.json"|])
        let assets = parseProjectAssets(projectAssetsJson)
        // Figure out name of output .dll
        let baseName = Path.GetFileNameWithoutExtension(fsproj.Name)
        let dllName = baseName + ".dll"
        // msbuild produces paths like src/LSP/bin/Debug/netcoreapp2.0/LSP.dll
        let target = FileInfo(Path.Combine [|fsproj.DirectoryName; "bin"; "Debug"; assets.framework; dllName|])
        // Get source info from .fsproj
        let project = restore(fsproj)
        let sources = 
            [ for i in project.GetItems("Compile") do 
                let relativePath = i.EvaluatedInclude
                let absolutePath = Path.Combine(fsproj.DirectoryName, relativePath)
                let normalizePath = Path.GetFullPath(absolutePath)
                yield FileInfo(normalizePath) ]
        // TODO project.assets.json specifies bin/placeholder/{project}.dll as dll, is this bad for performance?
        Ok({
            fsproj=fsproj
            target=target
            sources=sources
            projectReferences=assets.projects
            packageReferences=assets.packages
        })
    with e -> Error(e.Message)

let private pseudoProject = lazy(
    let start = FileInfo(Assembly.GetExecutingAssembly().Location).Directory
    dprintfn "Looking for PsuedoScript.fsproj relative to %s" start.FullName
    let rec walk(dir: DirectoryInfo) = 
        let candidate = FileInfo(Path.Combine [|dir.FullName; "client"; "PseudoScript.fsproj"|])
        if candidate.Exists then 
            candidate 
        elif dir = dir.Root then
            raise(Exception(sprintf "Couldn't find PseudoProject.fsproj in any parent directory of %A" start))
        else
            walk(dir.Parent)
    walk(start)
)

/// Get the baseline options for an .fsx script
/// In theory this should be done by FSharpChecker.GetProjectOptionsFromScript,
/// but it appears to be broken on dotnet core: https://github.com/fsharp/FSharp.Compiler.Service/issues/847
let scriptBase: Lazy<CrackedProject> = 
    lazy 
        match crack(pseudoProject.Value) with 
        | Ok(options) -> options 
        | Error(message) -> raise(Exception(sprintf "Failed to load PseudoScript.fsproj: %s" message))