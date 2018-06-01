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
| BadOptions of string

// Maintains caches of parsed versions of .fsprojOrFsx files
type ProjectManager(client: ILanguageClient, checker: FSharpChecker) = 
    // Index mapping .fs source files to .fsprojOrFsx project files that reference them
    let projectFileBySourceFile = new Dictionary<String, FileInfo>()
    // Cache the result of project cracking
    let analyzedByProjectFile = new Dictionary<String, ProjectOptions>()

    // When we analyze multiple project files, create a progress bar
    // These functions should be called in a `use` block to ensure the progress bar is closed:
    //   use notifyStartAnalyzeProjects(n)
    //   for ... do
    //     notifyAnalyzeProject(f)
    let notifyStartAnalyzeProjects(nFiles: int): IDisposable = 
        let message = JsonValue.Record [|   "title", JsonValue.String(sprintf "Analyze %d projects" nFiles )
                                            "nFiles", JsonValue.Number(decimal(nFiles)) |]
        client.CustomNotification("fsharp/startProgress", message)
        let notifyEnd() = client.CustomNotification("fsharp/endProgress", JsonValue.Null)
        { new IDisposable with member this.Dispose() = notifyEnd() }
    let notifyAnalyzeProject(fsprojOrFsx: FileInfo) = 
        client.CustomNotification("fsharp/incrementProgress", JsonValue.String(fsprojOrFsx.Name))

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

    // Analyze a project and add it to the cache
    let rec analyzeFsproj(fsproj: FileInfo) = 
        dprintfn "Analyzing %s" fsproj.Name
        notifyAnalyzeProject(fsproj)
        match ProjectCracker.crack(fsproj) with 
        | Error(e) -> addToCache(fsproj, BadOptions(e))
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
        if not (analyzedByProjectFile.ContainsKey(fsproj.FullName)) then 
            analyzeFsproj(fsproj)

    // Analyze multiple projects, with a progress bar
    let ensureAll(fs: FileInfo list) = 
        use progress = notifyStartAnalyzeProjects(List.length(fs))
        for f in fs do 
            if f.Name.EndsWith(".fsx") then 
                analyzeFsx(f)
            else if f.Name.EndsWith(".fsproj") then 
                ensureFsproj(f)
            else 
                dprintfn "Don't know how to analyze project %s" f.Name

    // Re-analyze all projects
    let resetCaches() = 
        let projects = [for f in analyzedByProjectFile.Keys do yield FileInfo(f)]
        dprintfn "Re-analyze %A" [for p in projects do yield p.Name]
        analyzedByProjectFile.Clear()
        projectFileBySourceFile.Clear()
        ensureAll(projects)
    member this.AddWorkspaceRoot(root: DirectoryInfo): Async<unit> = 
        async {
            let all = [
                for f in root.EnumerateFiles("*.fs*", SearchOption.AllDirectories) do 
                    if f.Name.EndsWith(".fsx") || f.Name.EndsWith(".fsproj") then 
                        yield f
            ]
            ensureAll(List.ofSeq(all))
        }
    member this.DeleteProjectFile(fsprojOrFsx: FileInfo) = 
        analyzedByProjectFile.Remove(fsprojOrFsx.FullName) |> ignore
        resetCaches()
    member this.PutProjectFile(fsprojOrFsx: FileInfo) = 
        resetCaches()
        ensureAll([fsprojOrFsx])
    member this.UpdateAssetsJson(assets: FileInfo) = 
        resetCaches()
    member this.FindProjectOptions(sourceFile: FileInfo): Result<FSharpProjectOptions, Diagnostic list> = 
        if projectFileBySourceFile.ContainsKey(sourceFile.FullName) then 
            let projectFile = projectFileBySourceFile.[sourceFile.FullName] 
            match analyzedByProjectFile.[projectFile.FullName] with 
            | FsprojOptions(_, options) | FsxOptions(options, []) -> Ok(options)
            | FsxOptions(_, errs) -> Error(Conversions.asDiagnostics(errs))
            | BadOptions(message) -> Error([Conversions.errorAtTop(message)])
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
                let deps = this.TransitiveDeps(FileInfo(fromProjectOptions.ProjectFileName))
                List.exists containsTarget deps
