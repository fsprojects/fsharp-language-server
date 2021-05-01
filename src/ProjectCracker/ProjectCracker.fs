module ProjectCracker

open LSP.Log
open System
open System.Diagnostics
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
    /// .fsproj files
    projectReferences: FileInfo list
    /// .dlls corresponding to non-F# projects
    otherProjectReferences: FileInfo list
    /// .dlls
    packageReferences: FileInfo list
    /// .dlls referenced with <Reference Include="..\Foo\Foo.dll" />
    directReferences: FileInfo list
    /// An error was encountered while cracking the project
    /// This message should be displayed at the top of every file
    error: string option
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
    projectName: string
    framework: string
    packages: FileInfo list
    projects: FileInfo list
}

type private Dep = {
    name: string
    version: string
}

type private CoreRuntime = {
    name: string
    majorVersion: int
    minorVersion: int
    path: string
}

type JsonValue with
    member x.GetCaseInsensitive(propertyName) =
        let mutable result: Option<JsonValue> = None
        for (k, v) in x.Properties do
            if String.Equals(propertyName, k, StringComparison.OrdinalIgnoreCase ) then
                result <- Some(v)
        result

let private frameworkPreference = [
    "net5.0", "net5.0";

    "netcoreapp3.1", ".NETCoreApp,Version=v3.1";
    "netcoreapp3.0", ".NETCoreApp,Version=v3.0";
    "netcoreapp2.2", ".NETCoreApp,Version=v2.2";
    "netcoreapp2.1", ".NETCoreApp,Version=v2.1";
    "netcoreapp2.0", ".NETCoreApp,Version=v2.0";
    "netcoreapp1.1", ".NETCoreApp,Version=v1.1";
    "netcoreapp1.0", ".NETCoreApp,Version=v1.0";

    "netstandard2.1", ".NETStandard,Version=v2.1";
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

let private parseProjectAssets(projectAssetsJson: FileInfo): ProjectAssets =
    dprintfn "Parsing %s" projectAssetsJson.FullName
    let root = JsonValue.Parse(File.ReadAllText(projectAssetsJson.FullName))
    let fsproj = FileInfo(root?project?restore?projectPath.AsString())
    // Find the assembly base name
    let projectName = root?project?restore?projectName.AsString()
    // Parses a version string into a version tuple
    let parseVersion(version: string): int * int =
        let components = version.Split([| '.' |])
        (int components.[0], int components.[1])
    // Determines the location of a Core runtime which matches the given version
    // range. Need to invoke `dotnet --list-runtimes` here which gives this output:
    //
    //   ...
    //     Microsoft.NETCore.App 2.2.8 [/usr/share/dotnet/shared/...]
    //     Microsoft.NETCore.App 3.0.1 [/usr/share/dotnet/shared/...]
    //
    // The path returned here will have framework DLLs which we need, but which may
    // not be explicitly referenced elsewhere
    let findRuntimePaths(): HashSet<CoreRuntime> =
        let dotnetProcess = new Process()
        dotnetProcess.StartInfo <- new ProcessStartInfo(FileName="dotnet",
                                                        Arguments="--list-runtimes",
                                                        CreateNoWindow=true,
                                                        UseShellExecute=false,
                                                        RedirectStandardOutput=true)
        dotnetProcess.Start() |> ignore
        dotnetProcess.WaitForExit(5000) |> ignore
        let stdoutRaw = dotnetProcess.StandardOutput.ReadToEnd()
        let stdout = stdoutRaw.Trim().Split('\n')
        let runtimePaths = HashSet<CoreRuntime>()

        for rawLine in stdout do
            let line = rawLine.Trim()

            let [| name; version; bracketPath |] = line.Trim().Split([| ' ' |], 3)
            let basePath = bracketPath.Substring(1, bracketPath.Length - 2)
            let (majorVersion, minorVersion) = parseVersion version

            // We want to traverse the dotnet packs directory, which will
            // have a more exact list of assemblies than what's in the runtime.
            // The runtime includes platform-specific assemblies with references that don't
            // always resolve outside of Windows.
            let dotnetRoot = Directory.GetParent(basePath).Parent.FullName
            let packBase = Path.Combine(dotnetRoot, "packs", name + ".Ref", version, "ref")

            // This only includes the version of netcoreapp corresponding to the actual
            // version of .NET Core. Since this may not match the project we'll have to
            // grab this from the filesystem.
            let packFrameworks =
                if Directory.Exists(packBase) then
                    List.ofSeq(Directory.EnumerateDirectories(packBase))
                else
                    []

            match packFrameworks with
            | packDir :: _ ->
                dprintfn "Discovered framework pack: %s v%s at %s" name version packDir
                runtimePaths.Add({name=name;
                                  majorVersion=majorVersion;
                                  minorVersion=minorVersion;
                                  path=packDir}) |> ignore
            | [] ->
                dprintfn "Discovered framework runtime: %s v%s at %s" name version basePath
                runtimePaths.Add({name=name;
                                  majorVersion=majorVersion;
                                  minorVersion=minorVersion;
                                  path=Path.Combine(basePath, version)}) |> ignore
        runtimePaths
    
    // Choose one of the frameworks listed in project.frameworks
    // by scanning all possible frameworks in order of preference
    let shortFramework, longFramework =
        let projectContainsFramework(short: string) =
            root?project?frameworks.TryGetProperty(short).IsSome
        let mutable found: (string * string) option = None
        for short, long in frameworkPreference do
            if projectContainsFramework(short) && found.IsNone then
                found <- Some(short, long)
        found.Value
    dprintfn "Chose framework %s / %s" shortFramework longFramework
    // Choose a version of a dependency by scanning targets
    let chooseVersion(dependencyName: string): string =
        let prefix = dependencyName + "/"
        let mutable found: string option = None
        for dependencyVersion, _ in root?targets.[longFramework].Properties do
            if dependencyVersion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && found.IsNone then
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
    let rec findTransitiveDeps(parent: Dep) =
        let nameVersion = parent.name + "/" + parent.version
        match root?targets.[longFramework].GetCaseInsensitive(nameVersion) with
        | None ->
            dprintfn "Couldn't find %s in targets" nameVersion
        | Some (lib) when transitiveDependencies.TryAdd(nameVersion, parent) ->
            match lib.TryGetProperty("dependencies") with
            | None -> ()
            | Some(next) ->
                for name, _ in next.Properties do
                    // The version in "dependencies" can be a range like [1.0, 2.0),
                    // so we will use the version from targets
                    let version = chooseVersion(name)
                    let child = {name=name; version=version}
                    let childNameVersion = child.name + "/" + child.version
                    if not(transitiveDependencies.ContainsKey(childNameVersion)) then
                        dprintfn "\t%s <- %s" nameVersion childNameVersion
                    findTransitiveDeps(child)
        | _ -> ()
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
    //             },
    //             "frameworkReferences": {
    //                 "Microsoft.NETCore.App": {
    //                     "privateAssets": "all"
    //                  }
    //             }
    //         }
    //     }
    // }
    let autoReferenced = HashSet<string>()
    for name, dep in root?project?frameworks.[shortFramework]?dependencies.Properties do
        if dep.TryGetProperty("autoReferenced") = Some(JsonValue.Boolean(true)) then
            autoReferenced.Add(name) |> ignore
        let version = chooseVersion(name)
        let dep = {name=name; version=version}
        findTransitiveDeps(dep)
    let [| _; longFrameworkVersion |] = 
      if longFramework.Contains("net5") then
        longFramework.Split("net")
      else
        longFramework.Split("Version=v")
    let (frameworkMajorVersion, frameworkMinorVersion) = parseVersion longFrameworkVersion
    let selectedRuntimes = Dictionary<string * int, CoreRuntime>()
    let runtimes = findRuntimePaths()
    match root?project?frameworks.[shortFramework].TryGetProperty("frameworkReferences") with
    | Some frameworkRefs ->
        for frameworkRef, _ in frameworkRefs.Properties do
            for runtime in runtimes do
                // .NET Core does not support forward compatibility across minor versions or any
                // compatibility between major versions. That means that the only case we need
                // to worry about is finding a preferred framework when two candidates share a
                // major version and both have a minor version at least as high as the target minor
                // version.
                //
                // In that case, we prefer the framework which is closest to the target. This avoids
                // drifting too far from the target framework (which could introduce extra API surface)
                // when we have a closer alternative.
                let versionMatch =
                    runtime.majorVersion = frameworkMajorVersion
                    && runtime.minorVersion >= frameworkMinorVersion
                if frameworkRef = runtime.name && versionMatch then
                    let runtimeKey = (runtime.name, runtime.majorVersion)
                    if selectedRuntimes.ContainsKey(runtimeKey) then
                        let previousMatch = selectedRuntimes.[runtimeKey]
                        if runtime.minorVersion < previousMatch.minorVersion then
                            selectedRuntimes.[runtimeKey] <- runtime
                    else
                        selectedRuntimes.[runtimeKey] <- runtime
    | None ->
        ()
    // ["/Users/georgefraser/.nuget/packages/", ...]
    let packageFolders = [for p, _ in root?packageFolders.Properties do yield p]

    // If the main version of the runtime differs from the target framework version, dotnet restore
    // will put a version of the .Ref into the NuGet packages directory. We can use this as an
    // alternative source of runtime directories if the exact version exists.
    //
    //   .../.nuget/packages/microsoft.netcore.app.ref/3.0.0/ref/netcoreapp3.0
    //
    // is preferred to this:
    //
    //   .../dotnet/packs/Microsoft.NETCore.App.Ref/3.1.0/ref
    //
    // for netcoreapp3.0
    for runtime in List.ofSeq(selectedRuntimes.Values) do
        let mutable currentRuntime = runtime

        for packageFolder in packageFolders do
            let nugetPackPath = Path.Combine(packageFolder, runtime.name.ToLower() + ".ref")
            if Directory.Exists(nugetPackPath) then
                for versionDir in Directory.GetDirectories(nugetPackPath) do
                    let version = Path.GetFileName(versionDir)
                    let (packMajorVersion, packMinorVersion) = parseVersion version
                    let versionMatch =
                        currentRuntime.majorVersion = packMajorVersion
                        && packMinorVersion >= frameworkMinorVersion

                    let packAssemblyPath = Path.Combine(nugetPackPath, version, "ref", shortFramework)
                    if versionMatch && packMinorVersion <= currentRuntime.minorVersion && Directory.Exists(packAssemblyPath) then
                        currentRuntime <- {currentRuntime with path=packAssemblyPath
                                                               minorVersion=packMinorVersion}

        let runtimeKey = (runtime.name, runtime.majorVersion)
        selectedRuntimes.[runtimeKey] <- currentRuntime
        dprintfn "Chose %s as the final path for runtime %s version (%d, %d)"
                 currentRuntime.path
                 currentRuntime.name
                 currentRuntime.majorVersion
                 currentRuntime.minorVersion

    // The runtime directory contains all of the framework DLLs that are implicitly
    // required, like System.Core.dll
    let runtimeAssemblies =
        [ for runtimeDir in selectedRuntimes.Values do yield! Directory.GetFiles(runtimeDir.path, "*.dll")]
    dprintfn "Discovered runtime assemblies %A" runtimeAssemblies
    // Find projects in libraries section
    for name, value in root?libraries.Properties do
        if value?``type``.AsString() = "project" then
            match name.Split('/') with
            | [|name; version|] ->
                let dep = {name=name; version=version}
                findTransitiveDeps(dep)
            | _ ->
                dprintfn "%s doesn't look like name/version" name
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
        let lib = root?libraries.GetCaseInsensitive(nameVersion).Value
        let prefix = lib?path.AsString()
        // For autoReferenced=true dependencies, we will include all dlls
        if autoReferenced.Contains(dep.name) then
            [ for json in lib?files.AsArray() do
                let f = json.AsString()
                if f.EndsWith(".dll") then
                    let relative = Path.Combine(prefix, f)
                    match absoluteDll(relative) with
                    | None -> ()
                    | Some(f) -> yield f ]
        // Otherwise, we'll look at the list of "compile" .dlls in "targets"
        else
            let target = root?targets.[longFramework].GetCaseInsensitive(nameVersion).Value
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
            [ for d in packageDlls @ runtimeAssemblies do
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
        projectName=projectName
        framework=shortFramework
        packages=List.map FileInfo packageDllsWithoutConflicts
        projects=projects
    }

let private project(fsproj: FileInfo): ProjectAnalyzer =
    let options = new AnalyzerManagerOptions()
    options.LogWriter <- !diagnosticsLog // TODO this doesn't follow ref changes
    let manager = AnalyzerManager(options)
    manager.GetProject(fsproj.FullName)

let private inferTargetFramework(fsproj: FileInfo): AnalyzerResult =
    let builds = project(fsproj).Build()
    // TODO get target framework from project.assets.json
    let mutable chosen: AnalyzerResult option = None
    for shortFramework, _ in frameworkPreference do
        if chosen.IsNone then
            for build in builds do
                if build.TargetFramework = shortFramework then
                    chosen <- Some(build)
    if chosen.IsNone then
        for build in builds do
            if chosen.IsNone then
                chosen <- Some(build)
    chosen.Value

let private projectTarget(csproj: FileInfo) =
    let baseName = Path.GetFileNameWithoutExtension(csproj.Name)
    let dllName = baseName + ".dll"
    let placeholderTarget = FileInfo(Path.Combine [|csproj.DirectoryName; "bin"; "Debug"; "placeholder"; dllName|])
    let projectAssetsJson = FileInfo(Path.Combine [|csproj.DirectoryName; "obj"; "project.assets.json"|])
    if projectAssetsJson.Exists then
        let assets = parseProjectAssets(projectAssetsJson)
        let dllName = assets.projectName + ".dll"
        // TODO this seems fragile
        FileInfo(Path.Combine [|csproj.DirectoryName; "bin"; "Debug"; assets.framework; dllName|])
    else
        placeholderTarget

let private absoluteIncludePath(fsproj: FileInfo, i: ProjectItem) =
    let relativePath = i.ItemSpec.Replace('\\', Path.DirectorySeparatorChar)
    let absolutePath = Path.Combine(fsproj.DirectoryName, relativePath)
    let normalizePath = Path.GetFullPath(absolutePath)
    FileInfo(normalizePath)

/// Crack an .fsproj file by:
/// - Running the "Restore" target and reading
/// - Reading .fsproj using the MSBuild API
/// - Reading libraries from project.assets.json
let crack(fsproj: FileInfo): CrackedProject =
    // Figure out name of output .dll
    let baseName = Path.GetFileNameWithoutExtension(fsproj.Name)
    let dllName = baseName + ".dll"
    let placeholderTarget = FileInfo(Path.Combine [|fsproj.DirectoryName; "bin"; "Debug"; "placeholder"; dllName|])
    try
        // Get source info from .fsproj
        let timeProject = Stopwatch.StartNew()
        let project = inferTargetFramework(fsproj)
        let sources =
            [ for KeyValue(k, v) in project.Items do
                if k = "Compile" then
                    for i in v do
                        yield absoluteIncludePath(fsproj, i) ]
        let directReferences =
            [ for KeyValue(k, v) in project.Items do
                if k = "Reference" then
                    for i in v do
                        yield absoluteIncludePath(fsproj, i) ]
        dprintfn "Cracked %s in %dms" fsproj.Name timeProject.ElapsedMilliseconds
        // Get package info from project.assets.json
        let projectAssetsJson = FileInfo(Path.Combine [|fsproj.DirectoryName; "obj"; "project.assets.json"|])
        if not(projectAssetsJson.Exists) then
            {
                fsproj=fsproj
                target=placeholderTarget
                sources=sources
                projectReferences=[]
                otherProjectReferences=[]
                packageReferences=[]
                directReferences=directReferences
                error=Some(sprintf "%s does not exist; maybe you need to build your project?" projectAssetsJson.FullName)
            }
        else
            let timeAssets = Stopwatch.StartNew()
            let assets = parseProjectAssets(projectAssetsJson)
            // msbuild produces paths like src/LSP/bin/Debug/netcoreapp2.0/LSP.dll
            // TODO this seems fragile
            let target = FileInfo(Path.Combine [|fsproj.DirectoryName; "bin"; "Debug"; assets.framework; dllName|])
            let isFsproj(f: FileInfo) = f.Name.EndsWith(".fsproj")
            let fsProjects, csProjects = List.partition isFsproj assets.projects
            let otherProjects = [for csproj in csProjects do yield projectTarget(csproj)]
            dprintfn "Cracked project.assets.json in %dms" timeAssets.ElapsedMilliseconds
            {
                fsproj=fsproj
                target=target
                sources=sources
                projectReferences=fsProjects
                otherProjectReferences=otherProjects
                packageReferences=assets.packages
                directReferences=directReferences
                error=None
            }
    with e ->
        dprintfn "Failed to build %s: %s\n%s" fsproj.Name e.Message e.StackTrace
        {
            fsproj=fsproj
            target=placeholderTarget
            sources=[]
            projectReferences=[]
            otherProjectReferences=[]
            packageReferences=[]
            directReferences=[]
            error=Some(e.Message)
        }
