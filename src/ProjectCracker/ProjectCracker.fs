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
open Buildalyzer

// Other points of reference:
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

type private Dep = {
    name: string
    version: string
    autoReferenced: bool
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
    // Choose a version of a dependency by scanning targets
    let chooseVersion(dependencyName: string): string = 
        let prefix = dependencyName + "/"
        let mutable found: string option = None 
        for dependencyVersion, _ in root?targets.[longFramework].Properties do 
            if dependencyVersion.StartsWith(prefix) && found.IsNone then 
                let version = dependencyVersion.Substring(prefix.Length)
                found <- Some(version)
        match found with 
        | Some(d) -> d
        | None -> 
            let keys = Array.map fst root?targets.[longFramework].Properties
            raise(Exception(sprintf "No version of %s found in %A" dependencyName keys))
    // All transitive dependencies of the project 
    // "FSharp.Core/4.3.4" => {FSharp.Core, 4.3.4, false}
    let transitiveDependencies = Dictionary<string, Dep>()
    // Find transitive dependencies by scanning the targets section
    // "targets": {
    //     ".NETStandard,Version=v2.0": {
    //         "NUnit/3.8.1": {
    //             "type": "package",
    //             "dependencies": {
    //                 "NETStandard.Library": "1.6.0",
    //                 "System.Runtime.Loader": "4.3.0",
    //                 "System.Threading.Thread": "4.3.0"
    //             },
    //             "compile": {
    //                 "lib/netstandard1.6/nunit.framework.dll": {}
    //             },
    //             "runtime": {
    //                 "lib/netstandard1.6/nunit.framework.dll": {}
    //             }
    //         },
    //     }
    // }
    let rec findTransitiveDeps(dep: Dep) = 
        let nameVersion = dep.name + "/" + dep.version
        if root?targets.[longFramework].TryGetProperty(nameVersion).IsNone then 
            dprintfn "Couldn't find %s in targets" nameVersion
        elif transitiveDependencies.TryAdd(nameVersion, dep) then 
            match root?targets.[longFramework].[nameVersion].TryGetProperty("dependencies") with 
            | None -> ()
            | Some(next) -> 
                for name, version in next.Properties do 
                    let dep = {name=name; version=version.AsString(); autoReferenced=false}
                    findTransitiveDeps(dep)
    // Find root dependencies by scanning the project section
    // "project": {
    //     "version": "1.0.0",
    //     "restore": { ... },
    //     "frameworks": {
    //         "netstandard2.0": {
    //             "dependencies": {
    //                 "FSharp.Core": {
    //                     "target": "Package",
    //                     "version": "[4.3.*, )"
    //                 },
    //                 "NETStandard.Library": {
    //                     "suppressParent": "All",
    //                     "target": "Package",
    //                     "version": "[2.0.1, )",
    //                     "autoReferenced": true
    //                 }
    //             }
    //         }
    //     }
    // }
    for name, dep in root?project?frameworks.[shortFramework]?dependencies.Properties do 
        let version = chooseVersion(name) 
        let autoReferenced = dep.TryGetProperty("autoReferenced") = Some(JsonValue.Boolean(true))
        let dep = {name=name; version=version; autoReferenced=autoReferenced}
        findTransitiveDeps(dep)
    // Find projects in libraries section
    for name, value in root?libraries.Properties do 
        if value?``type``.AsString() = "project" then 
            let [|name; version|] = name.Split('/')
            let dep = {name=name; version=version; autoReferenced=false}
            findTransitiveDeps(dep)
    dprintfn "Transitive dependencies are %A" (List.ofSeq(transitiveDependencies.Keys))
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
    // Find .dll files for each dependency
    // "targets": {
    //     ".NETCoreApp,Version=v2.0": {
    //         "FSharp.Core/4.3.4": {
    //             "type": "package",
    //             "compile": {
    //                 "lib/netstandard1.6/FSharp.Core.dll": {}
    //             },
    //             "runtime": {
    //                 "lib/netstandard1.6/FSharp.Core.dll": {}
    //             },
    //             "resource": { ... }
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
    let findDlls(dep: Dep) = 
        let nameVersion = dep.name + "/" + dep.version
        let lib = root?libraries.[nameVersion]
        let prefix = lib?path.AsString()
        // For autoReferenced=true dependencies, we will include all dlls
        if dep.autoReferenced then 
            [ for json in lib?files.AsArray() do 
                let f = json.AsString()
                if f.EndsWith(".dll") then
                    let relative = Path.Combine(prefix, f)
                    match absoluteDll(relative) with 
                    | None -> ()
                    | Some(f) -> yield f ]
        // Otherwise, we'll look at the list of "compile" .dlls in "targets"
        else 
            let target = root?targets.[longFramework].[nameVersion]
            match target.TryGetProperty("compile") with 
            | None -> 
                dprintfn "%s has no compile-time dependencies" nameVersion
                []
            | Some(map) -> 
                [ for dll, _ in map.Properties do 
                    let rel = Path.Combine(prefix, dll)
                    match absoluteDll(rel) with 
                    | None -> ()
                    | Some(abs) -> yield abs ]
    // Find all package dlls
    let packageDlls = [for d in transitiveDependencies.Values do yield! findDlls(d)]
    // Resolve conflicts by getting name and version from each DLL, choosing the highest version
    let packageDllsWithoutConflicts =
        let dllNameVersion = 
            [ for d in packageDlls do 
                match readAssembly(FileInfo(d)) with 
                | Error(e) -> dprintfn "Failed loading %s with error %s" d e
                | Ok(name, version) -> yield d, name, version ]
        let aName(dll, name, version) = name
        let aVersion(dll, name, version) = version
        let byName = List.groupBy aName dllNameVersion
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

let private load(fsproj: FileInfo): Project = 
    let options = new AnalyzerManagerOptions()
    options.LogWriter <- !diagnosticsLog // TODO this doesn't follow ref changes
    options.CleanBeforeCompile <- false
    let manager = AnalyzerManager(options)
    manager.GetProject(fsproj.FullName).Load()

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
        // TODO this seems fragile
        let target = FileInfo(Path.Combine [|fsproj.DirectoryName; "bin"; "Debug"; assets.framework; dllName|])
        // Get source info from .fsproj
        let project = load(fsproj)
        let sources = 
            [ for i in project.GetItems("Compile") do 
                let relativePath = i.EvaluatedInclude
                let absolutePath = Path.Combine(fsproj.DirectoryName, relativePath)
                let normalizePath = Path.GetFullPath(absolutePath)
                yield FileInfo(normalizePath) ]
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