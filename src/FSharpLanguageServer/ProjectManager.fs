namespace FSharpLanguageServer 

open LSP.Log
open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Xml
open LSP.Json
open LSP.Json.JsonExtensions
open LSP.Types
open Microsoft.FSharp.Compiler.SourceCodeServices
open ProjectCracker

type private ProjectOptions = 
| FsprojOptions of CrackedProject * FSharpProjectOptions 
| FsxOptions of FSharpProjectOptions * FSharpErrorInfo list 
| BadOptions of string * DateTime

// Maintains caches of parsed versions of .fsprojOrFsx files
type ProjectManager(client: ILanguageClient, checker: FSharpChecker) = 
    // Index mapping .fs source files to .fsprojOrFsx project files that reference them
    let projectFileBySourceFile = new Dictionary<String, FileInfo>()
    // Cache the result of project cracking
    let analyzedByProjectFile = new Dictionary<String, ProjectOptions>()

    let printOptions(options: FSharpProjectOptions) = 
        // This is long but it's useful
        dprintfn "%s: " options.ProjectFileName
        dprintfn "  ProjectFileName: %A" options.ProjectFileName
        dprintfn "  SourceFiles: %A" options.SourceFiles
        dprintfn "  ReferencedProjects: %A" [for dll, _ in options.ReferencedProjects do yield dll]
        dprintfn "  OtherOptions: %A" options.OtherOptions
        dprintfn "  LoadTime: %A" options.LoadTime
        dprintfn "  ExtraProjectInfo: %A" options.ExtraProjectInfo
        dprintfn "  IsIncompleteTypeCheckEnvironment: %A" options.IsIncompleteTypeCheckEnvironment
        dprintfn "  OriginalLoadReferences: %A" options.OriginalLoadReferences
        dprintfn "  ExtraProjectInfo: %A" options.ExtraProjectInfo
        dprintfn "  Stamp: %A" options.Stamp
        dprintfn "  UnresolvedReferences: %A" options.UnresolvedReferences
        dprintfn "  UseScriptResolutionRules: %A" options.UseScriptResolutionRules

    let addToCache(key: FileInfo, value: ProjectOptions) = 
        // Populate project file -> options cache
        analyzedByProjectFile.[key.FullName] <- value 
        // Populate source -> project cache
        let sources = 
            match value with 
            | FsprojOptions(_, options) -> options.SourceFiles
            | FsxOptions(options, _) -> options.SourceFiles
            | _ -> [||]
        for s in sources do 
            projectFileBySourceFile.[s] <- key

    // All transitive deps of an .fsproj file, including itself
    let transitiveDeps(projectFile: FileInfo) = 
        let touched = new HashSet<String>()
        let result = new System.Collections.Generic.List<FSharpProjectOptions>()
        let rec walk(key: string) = 
            if touched.Add(key) then 
                match analyzedByProjectFile.TryGetValue(key) with 
                | true, FsprojOptions(_, options) | true, FsxOptions(options, _) -> 
                    for _, parent in options.ReferencedProjects do 
                        walk(parent.ProjectFileName)
                    result.Add(options)
                | _, _ -> ()
        walk(projectFile.FullName)
        List.ofSeq(result)

    // When was this .fsx, .fsproj or corresponding project.assets.json file modified?
    // TODO use checksum instead of time
    let lastModified(fsprojOrFsx: FileInfo) = 
        let assets = FileInfo(Path.Combine [| fsprojOrFsx.Directory.FullName; "obj"; "project.assets.json" |])
        if assets.Exists then 
            max fsprojOrFsx.LastWriteTime assets.LastWriteTime
        else 
            fsprojOrFsx.LastWriteTime

    // Has this .fsproj or .fsx file or any of its transitive dependencies been modified?
    let needsUpdate(fsprojOrFsx: FileInfo) = 
        match analyzedByProjectFile.TryGetValue(fsprojOrFsx.FullName) with 
        | false, _ -> true 
        | _, FsprojOptions(_, options) -> 
            let deps = transitiveDeps(fsprojOrFsx)
            let modified = [for d in deps do yield lastModified(FileInfo(d.ProjectFileName))]
            let lastModified = List.max(modified)
            lastModified > options.LoadTime
        | _, FsxOptions(options, _) -> 
            lastModified(fsprojOrFsx) > options.LoadTime
        | _, BadOptions(_, checkedTime) -> 
            lastModified(fsprojOrFsx) > checkedTime

    // What projects need to be updated?
    let changedProjects() = 
        [for fileName in analyzedByProjectFile.Keys do 
            let file = FileInfo(fileName)
            if needsUpdate(file) then 
                yield file]

    // Analyze a script file and add it to the cache
    let analyzeFsx(fsx: FileInfo) = 
        dprintfn "Creating project options for script %s" fsx.Name
        let source = File.ReadAllText(fsx.FullName)
        let inferred, errors = checker.GetProjectOptionsFromScript(fsx.FullName, source, fsx.LastWriteTime, assumeDotNetFramework=false) |> Async.RunSynchronously
        let defaults = ProjectCracker.scriptBase.Value 
        let combinedOtherOptions = [|
            for o in inferred.OtherOptions do 
                yield o 
            for p in defaults.packageReferences do 
                // If a dll has already been included by GetProjectOptionsFromScript, skip it
                let matchesName(path: string) = path.EndsWith(p.Name)
                let alreadyIncluded = Array.exists matchesName inferred.OtherOptions
                if not(alreadyIncluded) then
                    yield "-r:" + p.FullName
        |]
        let options = {inferred with OtherOptions = combinedOtherOptions}
        printOptions(options)
        addToCache(fsx, FsxOptions(options, errors))

    // Analyze multiple projects, with a progress bar
    let ensureAll(fs: FileInfo list) =
        use progress = new ProgressBar(fs.Length, sprintf "Analyze %d projects" fs.Length, client, fs.Length <= 1)
        // Analyze a project and add it to the cache
        let rec analyzeFsproj(fsproj: FileInfo) = 
            dprintfn "Analyzing %s" fsproj.Name
            progress.Increment(fsproj)
            match ProjectCracker.crack(fsproj) with 
            | Error(e) -> addToCache(fsproj, BadOptions(e, fsproj.LastWriteTime))
            | Ok(cracked) -> 
                // Ensure we've analyzed all dependencies
                // We'll need their target dlls to form FSharpProjectOptions
                for r in cracked.projectReferences do 
                    ensureFsproj(r)
                // Convert to FSharpProjectOptions
                let options = {
                    ExtraProjectInfo = None 
                    IsIncompleteTypeCheckEnvironment = false 
                    LoadTime = fsproj.LastWriteTime
                    OriginalLoadReferences = []
                    OtherOptions = 
                        [|
                            // Dotnet framework should be specified explicitly
                            yield "--noframework"
                            // Reference output of other projects
                            for r in cracked.projectReferences do 
                                match analyzedByProjectFile.[r.FullName] with 
                                | FsprojOptions(cracked, _) -> yield "-r:" + cracked.target.FullName
                                | _ -> ()
                            // Reference packages
                            for r in cracked.packageReferences do 
                                yield "-r:" + r.FullName
                        |]
                    ProjectFileName = fsproj.FullName 
                    ReferencedProjects = 
                        [|
                            for r in cracked.projectReferences do 
                                match analyzedByProjectFile.[r.FullName] with 
                                | FsprojOptions(cracked, projectOptions) -> yield cracked.target.FullName, projectOptions
                                | _ -> ()
                        |]
                    SourceFiles = 
                        [|
                            for f in cracked.sources do 
                                yield f.FullName
                        |]
                    Stamp = None 
                    UnresolvedReferences = None 
                    UseScriptResolutionRules = false
                }
                // Cache inferred options
                addToCache(fsproj, FsprojOptions(cracked, options))
                // Log what we inferred
                printOptions(options)
        and ensureFsproj(fsproj: FileInfo) = 
            // TODO detect loop by caching set of `currentlyAnalyzing` projects
            if needsUpdate(fsproj) then 
                analyzeFsproj(fsproj)
        for f in fs do 
            if f.Name.EndsWith(".fsx") then 
                analyzeFsx(f)
            elif f.Name.EndsWith(".fsproj") then 
                ensureFsproj(f)
            else 
                dprintfn "Don't know how to analyze project %s" f.Name

    member this.AddWorkspaceRoot(root: DirectoryInfo): Async<unit> = 
        async {
            let all = [for f in root.EnumerateFiles("*.fs*", SearchOption.AllDirectories) do 
                        if f.Name.EndsWith(".fsx") || f.Name.EndsWith(".fsproj") then 
                            yield f]
            ensureAll(List.ofSeq(all))
        }
    member this.DeleteProjectFile(fsprojOrFsx: FileInfo) = 
        analyzedByProjectFile.Remove(fsprojOrFsx.FullName) |> ignore
        ensureAll(changedProjects())
    member this.NewProjectFile(fsprojOrFsx: FileInfo) = 
        ensureAll(fsprojOrFsx::changedProjects())
    member this.UpdateProjectFile(fsprojOrFsx: FileInfo) = 
        ensureAll(changedProjects())
    member this.UpdateAssetsJson(assets: FileInfo) = 
        ensureAll(changedProjects())
    member this.FindProjectOptions(sourceFile: FileInfo): Result<FSharpProjectOptions, Diagnostic list> = 
        if projectFileBySourceFile.ContainsKey(sourceFile.FullName) then 
            let projectFile = projectFileBySourceFile.[sourceFile.FullName] 
            match analyzedByProjectFile.[projectFile.FullName] with 
            | FsprojOptions(_, options) 
            | FsxOptions(options, []) -> Ok(options)
            | FsxOptions(_, errs) -> Error(Conversions.asDiagnostics(errs))
            | BadOptions(message, _) -> Error([Conversions.errorAtTop(message)])
        else Error([Conversions.errorAtTop(sprintf "No .fsproj or .fsx file references %s" sourceFile.FullName)])
    // All open projects, in dependency order
    // Ancestor projects come before projects that depend on them
    member this.OpenProjects: FSharpProjectOptions list = 
        let touched = new HashSet<String>()
        let result = new System.Collections.Generic.List<FSharpProjectOptions>()
        let rec walk(key: string) = 
            if touched.Add(key) then 
                match analyzedByProjectFile.[key] with 
                | FsprojOptions(_, options) | FsxOptions(options, []) -> 
                    for _, parent in options.ReferencedProjects do 
                        walk(parent.ProjectFileName)
                    result.Add(options)
                | _ -> ()
        for key in analyzedByProjectFile.Keys do 
            walk(key)
        List.ofSeq(result)
    // All transitive dependencies of `projectFile`, in dependency order
    member this.TransitiveDeps(projectFile: FileInfo): FSharpProjectOptions list =
        transitiveDeps(projectFile)
    // Is `targetSourceFile` visible from `fromSourceFile`?
    member this.IsVisible(targetSourceFile: FileInfo, fromSourceFile: FileInfo) = 
        match this.FindProjectOptions(fromSourceFile) with 
        | Error(_) -> false 
        | Ok(fromProjectOptions) ->
            // If fromSourceFile is in the same project as targetSourceFile, check if iFrom comes after iTarget in the source file order
            if Array.contains targetSourceFile.FullName fromProjectOptions.SourceFiles then 
                let iTarget = Array.IndexOf(fromProjectOptions.SourceFiles, targetSourceFile.FullName)
                let iFrom = Array.IndexOf(fromProjectOptions.SourceFiles, fromSourceFile.FullName)
                iFrom >= iTarget
            // Otherwise, check if targetSourceFile is in the transitive dependencies of fromProjectOptions
            else
                let containsTarget(dependency: FSharpProjectOptions) = Array.contains targetSourceFile.FullName dependency.SourceFiles
                let deps = transitiveDeps(FileInfo(fromProjectOptions.ProjectFileName))
                List.exists containsTarget deps
