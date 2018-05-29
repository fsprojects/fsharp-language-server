module ProjectCracker

open LSP.Log
open System
open System.IO
open Buildalyzer
open System.Xml
open LSP.Json
open LSP.Json.Ser
open Microsoft.Build.Framework
open Microsoft.Build.Logging
open Microsoft.Build.Execution

type CrackedProject = {
    // ?.fsproj file that was cracked
    fsproj: FileInfo 
    // ?.dll file built by this .fsproj file
    // Dependent projects will reference this dll in fscArgs, like "-r:?.dll"
    target: FileInfo
    // List of source files
    // These are fsc args, but presented separately because that's how FSharpProjectOptions wants them
    sources: FileInfo list
    // .fsproj files on references projects 
    projectReferences: FileInfo list 
    // .dlls
    packageReferences: FileInfo list 
}

let private files(fsproj: FileInfo, items: ProjectItemInstance seq) = 
    [ for i in items do 
        let relativePath = i.EvaluatedInclude
        let absolutePath = Path.Combine(fsproj.DirectoryName, relativePath)
        let normalizePath = Path.GetFullPath(absolutePath)
        yield FileInfo(normalizePath) ]

// TODO this is sort of horrible and doesn't even really work
// Maybe just do the msbuild path
module BackupCracker = 
    // Types to represent project.assets.json
    type Dependency = {
        ``type``: string 
        compile: Map<string, JsonValue>
        dependencies: Map<string, string>
    }
    type Library = {
        // Type of dependency. 'package' is the one we want
        ``type``: string 
        // Additional component of path to .dll, relative to packageFolders[?]
        path: string option
        // List of dlls, relative to packageFolders[?]/path
        files: string list
    }
    type ProjectFrameworkDependency = {
        target: string 
        version: string 
        autoReferenced: bool option
    }
    type ProjectFramework = {
        dependencies: Map<string, ProjectFrameworkDependency>
    }
    type Project = {
        frameworks: Map<string, ProjectFramework>
    }
    type ProjectAssets = {
        targets: Map<string, Map<string, Dependency>>
        libraries: Map<string, Library>
        packageFolders: Map<string, JsonValue>
        project: Project
    }

    let private fixPath(path: string): string = 
        path.Replace('\\', Path.DirectorySeparatorChar)
        
    let private doParseAssetsJson = deserializerFactory<ProjectAssets> defaultJsonReadOptions
    let private parseAssetsJson(jsonText: string) = 
        let jsonValue = JsonValue.Parse(jsonText)
        doParseAssetsJson jsonValue

    // Log messages once and then silence them
    let private alreadyLogged = System.Collections.Generic.HashSet<string>()
    let private logOnce(message: string): unit = 
        if not(alreadyLogged.Contains(message)) then 
            dprintfn "%s" message 
            alreadyLogged.Add(message) |> ignore
    // Parse a project.assets.json file
    let private parseAssets(path: FileInfo): ProjectAssets = 
        let text = File.ReadAllText(path.FullName)
        let parsed = parseAssetsJson(text)
        parsed
    // Target frameworks, in descending order of preference
    let private targetFrameworks = [
        "netcoreapp2.1", ".NETCoreApp,Version=v2.1";
        "netcoreapp2.0", ".NET,Version=v2.0";
        "netcoreapp1.1", ".NET,Version=v1.1";
        "netcoreapp1.0", ".NET,Version=v1.0";

        "netstandard2.0", ".NETStandard,Version=v2.0";
        "netstandard1.6", ".NET,Version=v1.6";
        "netstandard1.5", ".NET,Version=v1.5";
        "netstandard1.4", ".NET,Version=v1.4";
        "netstandard1.3", ".NET,Version=v1.3";
        "netstandard1.2", ".NET,Version=v1.2";
        "netstandard1.1", ".NET,Version=v1.1";
        "netstandard1.0", ".NET,Version=v1.0";

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
        "net11", ".NETFramework,Version=v1.1";
    ]
    let private preferredFramework(options: string list): string = 
        let mutable result: string option = None 
        for shortName, longName in targetFrameworks do 
            if result.IsNone && List.contains shortName options then 
                result <- Some(shortName)
        match result with 
        | Some name -> name
        | None -> raise(Exception(sprintf "Didn't recognize any of the .net frameworks in %A" options))
    let private longFrameworkName(findShortName: string) = 
        let mutable result: string option = None 
        for shortName, longName in targetFrameworks do 
            if result.IsNone && shortName = findShortName then 
                result <- Some(longName)
        match result with 
        | Some name -> name
        | None -> raise(Exception(sprintf "Didn't the .net frameworks %A" findShortName))
    // Find all dlls in project.assets.json
    let private findLibraryDlls(targetFramework: string, assets: ProjectAssets): FileInfo list = 
        let targetFrameworkLongName = longFrameworkName(targetFramework)
        // Given a dependency name, for example FSharp.Core, lookup the version in $.libraries, for example FSharp.Core/4.3.4
        let lookupVersion(dependencyName: string) = 
            let mutable found: string option = None
            for KeyValue(dependencyVersion, library) in assets.libraries do 
                if dependencyVersion.StartsWith(dependencyName + "/") && found = None then 
                    found <- Some(dependencyVersion)
            found
        // Find all dependencies in $.project.frameworks with autoReferenced=true,
        // We will import the whole contents of these dependencies
        let autoReferenced = seq {
            // TODO this takes the union of all target frameworks. Should we just pick the first one?
            for KeyValue(frameworkName, framework) in assets.project.frameworks do 
                for KeyValue(dependencyName, dependency) in framework.dependencies do 
                    if dependency.autoReferenced = Some true then 
                        match lookupVersion(dependencyName) with 
                        | None -> ()
                        | Some(name) -> yield name
        }
        // Identify which files are called out in the keys of in $.targets[*][dep/version].compile
        let compileFiles = seq {
            let libraryMap = assets.targets.[targetFrameworkLongName]
            for KeyValue(dependencyName, dependency) in libraryMap do 
                for KeyValue(dll, _) in dependency.compile  do 
                    if dll.EndsWith(".dll") then 
                        yield (dependencyName, dll)
        }
        // Look up every autoReferenced dependency in $.libraries and include all DLLs 
        let autoReferencedFiles = seq {
            for dependency in autoReferenced do 
                if assets.libraries.ContainsKey(dependency) then 
                    for dll in assets.libraries.[dependency].files do 
                        if dll.EndsWith(".dll") then 
                            yield (dependency, dll)
                else logOnce(sprintf "Couldn't find auto-referenced dependency %s in libraries" dependency)
        }
        let allFiles = Set.ofSeq(Seq.concat [compileFiles; autoReferencedFiles])
        // Look up each dependency in $.libraries[dep/version].files
        let libraryFile(dependency: string, dll: string) = seq {
            if assets.libraries.ContainsKey(dependency) then 
                let library = assets.libraries.[dependency]
                match library.path with 
                | None -> dprintfn "Skipping %s because no path in %A" dependency library
                | Some(parentPath) -> 
                    if List.contains dll library.files then 
                        yield Path.Combine(parentPath, dll)
                    else 
                        logOnce(sprintf "DLL %s is not in libraries[%s].files" dll dependency)
            else logOnce(sprintf "Dependency %s not in libraries" dependency)
        }
        let files = Seq.collect libraryFile allFiles
        // Find .dlls by checking each key of $.packageFolders
        let findAbsolutePath(relativePath: string): FileInfo option = 
            let mutable found: FileInfo option = None
            for KeyValue(packageFolder, _) in assets.packageFolders do 
                let absolutePath = Path.Combine(packageFolder, relativePath)
                let normalizePath = Path.GetFullPath(absolutePath)
                if File.Exists(normalizePath) && found = None then
                    found <- Some(FileInfo(normalizePath))
            found
        [ for f in files do 
            match findAbsolutePath(f) with 
            | None -> ()
            | Some found -> yield found ]

    // Crack an .fsproj file by running the "Restore" target and reading project.assets.json
    let crackWithProjectAssetsJson(fsproj: FileInfo): Result<CrackedProject, string> = 
        try 
            let manager = new AnalyzerManager()
            let analyzer = manager.GetProject(fsproj.FullName)
            // Invoke Restore, then build
            let project = analyzer.Load()
            let compile = project.CreateProjectInstance()
            let consoleLogger = ConsoleLogger(LoggerVerbosity.Normal, WriteHandler(dprintfn "%s"), ColorSetter(ignore), ColorResetter(ignore))
            let iLogger = consoleLogger :> ILogger
            compile.Build("Restore", [iLogger]) |> ignore 
            // Get sources and project references from .fsproj
            let sources = compile.GetItems("Compile")
            let projectReferences = compile.GetItems("ProjectReference")
            // Get as much as possible from project.assets.json
            // This was generated by *some* successful build, so it's more reliable than the introspection API
            let projectAssetsJsonFile = FileInfo(Path.GetFullPath(Path.Combine [|fsproj.FullName; ".."; "obj"; "project.assets.json"|]))
            if not(projectAssetsJsonFile.Exists) then 
                Error(sprintf "%s does not exist; maybe you need to build your project?" projectAssetsJsonFile.FullName)
            else
                let assets = parseAssets(projectAssetsJsonFile)
                // Names of target frameworks, for example net45, netstandard2.0
                let targetFrameworks = [for KeyValue(t, _) in assets.project.frameworks do yield t]
                let targetFramework = preferredFramework(targetFrameworks)
                // Figure out name of output .dll
                // Even if this is wrong, it's still usable by the F# Compiler
                let targetName = fsproj.Name.Substring(0, fsproj.Name.Length - ".fsproj".Length) + ".dll"
                let targetPath = Path.Combine [|fsproj.FullName; ".."; "obj"; "Debug"; targetFramework; targetName|]
                let target = FileInfo(Path.GetFullPath(targetPath))
                // Get .dlls
                let packageReferences = findLibraryDlls(targetFramework, assets)
                Ok({
                    fsproj=fsproj 
                    target=target
                    sources=files(fsproj, sources)
                    projectReferences=files(fsproj, projectReferences)
                    packageReferences=packageReferences
                })
        with e -> Error(e.Message)

// Crack an .fsproj file by running the "Compile" target and asking MSBuild what to do
let crackWithCompile(fsproj: FileInfo): Result<CrackedProject, string> = 
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

// Extract the key information we need to form FSharpProjectOptions from a .fsproj file
let crack(fsproj: FileInfo): Result<CrackedProject, string> = 
    // Plan A: run the "Compile" target and get MSBuild to tell us what we need to know
    match crackWithCompile(fsproj) with 
    | Ok(cracked) -> Ok(cracked)
    | Error(e) -> 
        dprintfn "Building %s failed with %s; trying to parse project.assets.json..." fsproj.Name e
        // Plan B: run the "Restore" target, get source file info from MSBuild, get most information from project.assets.json
        BackupCracker.crackWithProjectAssetsJson(fsproj)