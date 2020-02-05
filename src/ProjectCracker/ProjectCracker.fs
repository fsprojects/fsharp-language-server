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
open System.Linq
open Microsoft.Build.Execution

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
    /// System assemblies referenced with <Reference Include="System.Data" />
    systemReferences: string list
    /// An error was encountered while cracking the project
    /// This message should be displayed at the top of every file
    error: string option
}

let mutable includeCompileBeforeItems = false

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

[<Literal>]
let private p_netstandard = "netstandard"
[<Literal>]
let private p_netcoreapp = "netcoreapp"
[<Literal>]
let private p_netfx = "net"

type private TFM =
    | NetFX        of int
    | NetCoreApp   of int
    | NetStandard  of int
    | Other
    with 
    member this.SortIndex =
        match this with 
        | NetFX v -> v-400
        | NetStandard v -> v 
        | NetCoreApp v -> v | Other -> 0
    static member Parse(tfm: string) =
        let (|FL|INT|STR|) (x: string) =
            match Int32.TryParse x with
            | true, v -> INT v
            | _ -> 
            match Double.TryParse x with
            | true, v -> FL v
            | _ -> STR x
        let (|FX|STD|COREAPP|OTHER|) (tfm: string) =
            if tfm.StartsWith p_netstandard then
                STD(tfm.Substring p_netstandard.Length)
            elif tfm.StartsWith p_netcoreapp then
                COREAPP(tfm.Substring p_netcoreapp.Length)
            elif tfm.StartsWith p_netfx then
                FX(tfm.Substring p_netfx.Length)
            else
                OTHER

        match tfm with
        | FX(INT ver)     -> let ver = if ver < 100 then ver * 10 else ver
                             in NetFX(ver)
        | COREAPP(FL ver) -> NetCoreApp(int <| ver*100.0)
        | STD(FL ver)     -> NetStandard(int <| ver*100.0)
        | _               -> Other


type JsonValue with
    member x.GetCaseInsensitive(propertyName) = 
        let mutable result: Option<JsonValue> = None
        for (k, v) in x.Properties do 
            if String.Equals(propertyName, k, StringComparison.OrdinalIgnoreCase ) then 
                result <- Some(v)
        result

let private dotnetPackFolders =
    use proc = new Process()
    proc.StartInfo.UseShellExecute <- false
    proc.StartInfo.FileName <- "dotnet"
    proc.StartInfo.CreateNoWindow <- true
    proc.StartInfo.Arguments <- "--list-sdks"
    proc.StartInfo.RedirectStandardOutput <- true
    proc.Start() |> ignore
    proc.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
    |> Seq.map (fun x ->
        dprintfn "output: %s" x
        let i  = x.IndexOf('[') + 1
        let i' = x.LastIndexOf(']')
        Path.Combine(x.Substring(i, i' - i), "..", "packs") |> Path.GetFullPath
       )
    |> Seq.distinct
    |> List.ofSeq

dprintfn "dotnet pack folders: %A" dotnetPackFolders

let private frameworkPreference = [
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

    "net48", ".NETFramework,Version=v4.8";
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

let private projectAssets = new Dictionary<String, String>()

type DotnetSdk =
    {
        Version: string
        Path: string
    }

let private dotnetSdks =
    try
        use proc = new System.Diagnostics.Process()
        proc.StartInfo.UseShellExecute <- false
        proc.StartInfo.FileName <- "dotnet"
        proc.StartInfo.CreateNoWindow <- true
        proc.StartInfo.Arguments <- "--list-sdks"
        proc.StartInfo.RedirectStandardOutput <- true
        proc.Start() |> ignore
        proc.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
        |> Seq.map (fun x ->
            let i  = x.IndexOf('[') + 1
            let i' = x.LastIndexOf(']')
            let sdkver = x.Substring(0, i - 1).Trim()
            let basedir = x.Substring(i, i' - i).Trim()
            {
                Version = sdkver
                Path = Path.Combine(basedir, sdkver) |> Path.GetFullPath
            }
           )
        |> List.ofSeq
    with _ -> []
let private latestSdk = dotnetSdks |> List.sortByDescending (fun x -> x.Version) |> List.tryHead  

type internal HostCompile() =
  member th.Compile(_:obj, _:obj, _:obj) = 0
  interface ITaskHost

let private project(fsproj: FileInfo): ProjectInstance = 
    let fsprojAbsDirectory = Path.GetDirectoryName fsproj.FullName

    use _pwd = 
        let dir = Directory.GetCurrentDirectory()
        Directory.SetCurrentDirectory(fsprojAbsDirectory)
        { new System.IDisposable with
            member x.Dispose() = Directory.SetCurrentDirectory(dir) }

    let sdk = latestSdk.Value.Path
    let globalProperties = Map.ofList [
        // https://daveaglick.com/posts/running-a-design-time-build-with-msbuild-apis
        "SolutionDir", fsprojAbsDirectory
        "MSBuildExtensionsPath", sdk
        "MSBuildSDKsPath", Path.Combine(sdk, "Sdks")
        "RoslynTargetsPath", Path.Combine(sdk, "Roslyn")
    ]

    Environment.SetEnvironmentVariable(
        "MSBuildExtensionsPath",
        globalProperties.["MSBuildExtensionsPath"])
    Environment.SetEnvironmentVariable(
        "MSBuildSDKsPath",
        globalProperties.["MSBuildSDKsPath"]);

    // not cool!
    let toolsVersion =
        if Directory.Exists(Path.Combine(sdk, "Current")) then "Current"
        else ToolLocationHelper.CurrentToolsVersion

    use engine = new Microsoft.Build.Evaluation.ProjectCollection(globalProperties)
    engine.AddToolset(Toolset(toolsVersion, sdk, engine, String.Empty))
    let host = new HostCompile()
    engine.HostServices.RegisterHostObject(fsproj.FullName, "CoreCompile", "Fsc", host)

    use file = new FileStream(fsproj.FullName, FileMode.Open, FileAccess.Read, FileShare.Read)
    use stream = new StreamReader(file)
    use xmlReader = System.Xml.XmlReader.Create(stream)

    let project =
        try
            engine.LoadProject(xmlReader, FullPath=fsproj.FullName, toolsVersion=toolsVersion)
        with
        | exn -> 
            let tools = engine.Toolsets |> Seq.map (fun x -> x.ToolsPath) |> Seq.toList
            raise (new Exception(sprintf "Could not load project %s in ProjectCollection. Available tools: %A. ToolsVersion: %s. Message: %s" fsproj.FullName tools toolsVersion exn.Message))
        
    project.SetGlobalProperty("ShouldUnsetParentConfigurationAndPlatform", "false") |> ignore
    let projInstance = project.CreateProjectInstance()

    let logger = {
        new ILogger with 
        member x.Initialize(src) = src.AnyEventRaised.Add(fun x -> dprintfn "msbuild: %s" x.Message)
        member x.Shutdown() = ()
        member x.get_Parameters() = ""
        member x.set_Parameters(_) = ()
        member x.get_Verbosity() = LoggerVerbosity.Normal
        member x.set_Verbosity(_) = ()
    }

    projInstance.Build([| "Build" |], [logger]) |> ignore
    projInstance

let private getprop (p: ProjectInstance) s =
    let v = p.GetPropertyValue s
    if String.IsNullOrWhiteSpace v then None
    else Some v

let mkAbsolute dir (v: string) = 
    if Path.IsPathRooted v then v
    else Path.Combine(dir, v)


let getItems (p: ProjectInstance) s = [ for f in p.GetItems(s) -> mkAbsolute p.Directory f.EvaluatedInclude ]

let getAssets (fsproj: ProjectInstance) =

    //let outFileOpt = getprop projInstance "TargetPath"
    let projfile = Path.GetFullPath(fsproj.ProjectFileLocation.File)
    let mutable assets = ""
    if projectAssets.TryGetValue(projfile, &assets) then
        FileInfo(assets)
    else

    let msbuildprops =
        try
            Path.Combine [|fsproj.Directory; (getprop fsproj "BaseIntermediateOutputPath").Value|]
        with | ex -> 
            dprintfn "ProjectManager: msbuildprops: %s" <| ex.ToString()
            Path.Combine [| fsproj.Directory; "obj" |]

    let assets = msbuildprops
                 |> (fun p -> Path.Combine [| p; "project.assets.json" |])
                 |> FileInfo

    projectAssets.[projfile] <- assets.FullName
    assets


let private inferTargetFramework(fsproj: ProjectInstance): string= 
    let tfms = getprop fsproj "TargetFrameworks"
    let tfm = getprop fsproj "TargetFramework"

    let tfms = 
        match tfms, tfm with
        | Some tfms, _ ->
            tfms.Split(';')
        | _, Some tfm -> [| tfm |]
        | _ -> [|"netcoreapp2.0"|]

    tfms 
    |> Seq.sortByDescending (fun x -> (TFM.Parse x).SortIndex)
    |> Seq.head

let invalidateProjectAssets (fsprojOrFsx: FileInfo) =
    projectAssets.Remove(fsprojOrFsx.FullName) |> ignore

let getProject(assets: FileInfo) =
    seq {
        for KeyValue(k,v) in projectAssets do
            if v = assets.FullName then
                yield FileInfo k
    }

let private projectTarget(csproj: FileInfo) = 
    let proj = project(csproj)
    let dllName = (getprop proj "AssemblyName").Value + ".dll"
    let dllPath = Path.Combine [|csproj.DirectoryName; (getprop proj "OutputPath").Value; dllName |]
    FileInfo(dllPath)

let private parseProjectAssets(projectAssetsJson: FileInfo): ProjectAssets = 
    dprintfn "Parsing %s" projectAssetsJson.FullName
    let root = JsonValue.Parse(File.ReadAllText(projectAssetsJson.FullName))
    let fsproj = FileInfo(root?project?restore?projectPath.AsString())  
    // Find the assembly base name
    let projectName = root?project?restore?projectName.AsString()
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

    let projectFrameworks = root?project?frameworks.[shortFramework]
    let targets = root?targets.[longFramework]

    // Choose a version of a dependency by scanning targets
    let chooseVersion(dependencyName: string): string = 
        let prefix = dependencyName + "/"
        let mutable found: string option = None 
        for dependencyVersion, _ in targets.Properties do 
            if dependencyVersion.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && found.IsNone then 
                let version = dependencyVersion.Substring(prefix.Length)
                found <- Some(version)
        match found with 
        | Some(d) -> d
        | None -> 
            let keys = Array.map fst targets.Properties
            raise(Exception(sprintf "No version of %s found in %A" dependencyName keys))

    let projectTFM = TFM.Parse shortFramework
    let tfmCompatible lib =
        let lib = TFM.Parse lib
        match lib, projectTFM with
        | NetFX v1,       NetFX v2 when v1 <= v2 -> Some lib
        | NetStandard v1, NetStandard v2 when v1 <= v2 -> Some lib
        | NetCoreApp v1,  NetCoreApp v2 when v1 <= v2 -> Some lib
        | NetStandard v1, NetCoreApp v2 when v1 <= v2 -> Some lib
        | NetStandard v1, NetFX 450 when v1 <= 110 -> Some lib
        | NetStandard v1, NetFX 451 when v1 <= 120 -> Some lib
        | NetStandard v1, NetFX 460 when v1 <= 130 -> Some lib
        | NetStandard v1, NetFX 461 when v1 <= 200 -> Some lib
        | _ -> None

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
        match targets.GetCaseInsensitive(nameVersion) with 
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

    // Find root dependencies by scanning the project section.
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

    let autoReferenced = HashSet<string>()
    for name, dep in projectFrameworks?dependencies.Properties do 
        if dep.TryGetProperty("autoReferenced") = Some(JsonValue.Boolean(true)) then 
            autoReferenced.Add(name) |> ignore
        let version = chooseVersion(name)
        let dep = {name=name; version=version}
        findTransitiveDeps(dep)

    // Find projects in libraries section
    for name, value in root?libraries.Properties do 
        if value?``type``.AsString() = "project" then 
            match name.Split('/') with
            | [|name; version|] -> 
                let dep = {name=name; version=version}
                findTransitiveDeps(dep)
            | _ -> 
                dprintfn "%s doesn't look like name/version" name
    // ["/Users/georgefraser/.nuget/packages/", ...]
    let packageFolders = [for p, _ in root?packageFolders.Properties do yield p]

    // Search package folders for a .dll
    let absoluteDll(relativeToPackageFolder: string): string option = 
        if not <| relativeToPackageFolder.EndsWith(".dll") then None
        else

        let mutable found: string option = None
        for packageFolder in packageFolders do 
            let candidate = Path.Combine(packageFolder, relativeToPackageFolder)
            if File.Exists(candidate) && found.IsNone then 
                found <- Some(candidate)
        if found.IsNone then 
            dprintfn "Couldn't find %s in %A" relativeToPackageFolder packageFolders 
        found

    // Search packs for *.Ref assemblies
    let absolutePackRef(name, ver) =
        dprintfn "absolutePackRef: name=%s ver=%s" name ver

        let packDirs = packageFolders @ dotnetPackFolders
        let getRefDir (pdir: string) = Path.Combine(pdir, name + ".Ref")
        let refDirs =
            if ver = "*" then
                packDirs 
                |> List.collect (fun pdir -> 
                    try
                        let dir = DirectoryInfo(getRefDir pdir)
                        if not dir.Exists then []
                        else

                        dir.EnumerateDirectories()
                        |> Seq.map (fun (v: DirectoryInfo) -> Path.Combine(v.FullName, "ref", shortFramework))
                        |> List.ofSeq
                    with _ -> []
                )
            else
                packDirs
                |> List.map (fun pdir -> Path.Combine(getRefDir pdir, ver, "ref", shortFramework))

        refDirs
        |> List.collect(fun p ->
            try
                let di = DirectoryInfo(p)
                di.EnumerateFiles("*.dll") 
                |> Seq.map (fun x -> x.FullName)
                |> List.ofSeq
            with ex -> [])

    // Find .dll files for each dependency
    // Additionally, for netcoreapp3.0+ and netstandard2.1+, 
    // find Microsoft.NETCore.App.Ref / NETStandard.Library.Ref assemblies.
    // Sample:
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
        let autoref = autoReferenced.Contains(dep.name)

        // For autoReferenced=true dependencies, we will include all dlls
        // Note, assemblies in directories other than lib/ and ref/ may have different
        // runtime requirements than the current inferred runtime (e.g. native dlls in runtimes/).
        match projectTFM, autoref with
        | (NetCoreApp v), true when v >= 300 ->
            absolutePackRef(dep.name, dep.version)
        | (NetStandard v), true when v >= 210 ->
            absolutePackRef(dep.name, dep.version)
        | _, true ->
            lib?files.AsArray() 
            |> Seq.choose (fun json ->
                let f = json.AsString()
                match f.Split('/') with
                | [| ftype; tfm; dll |] when dll.EndsWith(".dll") && (ftype = "lib" || ftype = "ref") -> 
                    let rel = Path.Combine(prefix, f)
                    match tfmCompatible tfm, absoluteDll(rel) with 
                    | Some tfm, Some abs -> Some(tfm, dll, abs)
                    | _ ->None 
                | _ -> None) 
            |> Seq.groupBy (fun (_,dll,_) -> dll)
            |> Seq.map (fun (_: string, xs) -> 
                xs
                |> Seq.sortByDescending (fun (tfm, _, _) -> tfm.SortIndex )
                |> Seq.head)
            |> Seq.map (fun (_, _, f) -> f)
            |> List.ofSeq
        // Otherwise, we'll look at the list of "compile" .dlls in "targets"
        | _ ->
            let target = targets.GetCaseInsensitive(nameVersion).Value
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

    // netcore 3.0.100-preview8-013656 and above: 
    // Find frameworkReferences and reference the Ref packs
    // "project": {
    //     "version": "1.0.0",
    //     "restore": { ... },
    //     "frameworks": {
    //         "netcoreapp3.0": {
    //             "frameworkReferences": {
    //                 "Microsoft.NETCore.App": {
    //                   "privateAssets": "all"
    //                 }
    //             }
    //         }
    //     }
    // }
    let frameworkReferences = 
        match projectFrameworks.TryGetProperty("frameworkReferences"), projectFrameworks.TryGetProperty("runtimeIdentifierGraphPath") with
        | Some refs, Some runtimeIdentifierGraphPath -> 
            let rid_ver = (runtimeIdentifierGraphPath.AsString()) |> Path.GetDirectoryName |> Path.GetFileName
            [ for frameworkReference, _ in refs.Properties do 
                dprintfn "frameworkReference: %s %A" frameworkReference rid_ver
                yield frameworkReference ]
        | _ -> []

    dprintfn "frameworkReferences: %A" frameworkReferences

    // Find all package dlls
    let packageDlls = seq {
        for d in transitiveDependencies.Values do 
            yield! findDlls(d)
        for f in frameworkReferences do
            yield! absolutePackRef(f, "*")
    }


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
        projectName=projectName
        framework=shortFramework
        packages=List.map FileInfo packageDllsWithoutConflicts
        projects=projects
    }

/// Crack an .fsproj file by:
/// - Running the "Restore" target and reading 
/// - Reading .fsproj using the MSBuild API
/// - Reading libraries from project.assets.json
let crack(fsproj: FileInfo): CrackedProject = 
    try 
        // Get source info from .fsproj
        let proj = project(fsproj)
        let timeProject = Stopwatch.StartNew()
        let tfm = inferTargetFramework(proj)
        let mkAbsolute = mkAbsolute proj.Directory
        let infoList = List.map FileInfo
        let sources = 
            [ for x in getItems proj "Compile" -> mkAbsolute x ]
            @ if includeCompileBeforeItems then [ for x in getItems proj "CompileBefore" -> mkAbsolute x ]
              else []
        let references = [ for x in getItems proj "Reference" -> x ]

        let directReferences, systemReferences =
            let t, f = List.partition (fun (x: string) -> x.EndsWith(".dll")) references
            t |> List.map (mkAbsolute),
            f |> List.map (Path.GetFileName)

        dprintfn "Cracked %s in %dms" fsproj.Name timeProject.ElapsedMilliseconds
        // Get package info from project.assets.json
        let projectAssetsJson = getAssets proj
        let outputPath        = (getprop proj "OutputPath").Value
        let dllName           = (getprop proj "AssemblyName").Value + ".dll"
        let dllTarget         = FileInfo(Path.Combine [|outputPath; dllName|])
        if not(projectAssetsJson.Exists) then
            {
                fsproj                 = fsproj
                target                 = dllTarget
                sources                = infoList sources
                projectReferences      = []
                otherProjectReferences = []
                packageReferences      = []
                directReferences       = infoList directReferences
                systemReferences       = systemReferences
                error                  = Some(sprintf "%s does not exist; maybe you need to build your project?" projectAssetsJson.FullName)
            }
        else
            let timeAssets             = Stopwatch.StartNew()
            let assets                 = parseProjectAssets(projectAssetsJson)
            let target                 = FileInfo(Path.Combine [|outputPath; dllName|])
            let isFsproj(f: FileInfo)  = f.Name.EndsWith(".fsproj")
            let fsProjects, csProjects = List.partition isFsproj assets.projects
            let otherProjects          = [for csproj in csProjects do yield projectTarget(csproj)]
            dprintfn "Cracked %s in %dms" projectAssetsJson.FullName timeAssets.ElapsedMilliseconds
            {
                fsproj=fsproj
                target=target
                sources=infoList sources
                projectReferences=fsProjects
                otherProjectReferences=otherProjects
                packageReferences=assets.packages
                directReferences=infoList directReferences
                systemReferences=systemReferences
                error=None
            }
    with e -> 
        let baseName = Path.GetFileNameWithoutExtension(fsproj.Name)
        let dllName = baseName + ".dll"
        let placeholderTarget = FileInfo(Path.Combine [|Path.GetDirectoryName(fsproj.FullName); "bin"; "Debug"; "placeholder"; dllName|])
        dprintfn "Failed to build %s: %s" fsproj.Name e.Message
        {
            fsproj=fsproj
            target=placeholderTarget
            sources=[]
            projectReferences=[]
            otherProjectReferences=[]
            packageReferences=[]
            directReferences=[]
            systemReferences=[]
            error=Some(e.Message)
        }
