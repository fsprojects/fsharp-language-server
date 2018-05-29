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
open Microsoft.VisualBasic.CompilerServices
open Microsoft.FSharp.Compiler.SourceCodeServices
open Projects.ProjectParser

// Maintains caches of parsed versions of .fsproj files
type ProjectManager(client: ILanguageClient) = 
    // Index mapping .fs source files to .fsproj project files that reference them
    let projectFileBySourceFile = new Dictionary<String, FileInfo>()
    // Cache the result of project cracking
    let analyzedByProjectFile = new Dictionary<String, Result<CrackedProject * FSharpProjectOptions, string>>()

    // When we analyze multiple project files, create a progress bar
    // These functions should be called in a `use` block to ensure the progress bar is closed:
    //   use notifyStartAnalyzeProjects(n)
    //   for ... do
    //     notifyAnalyzeProject(f)
    let notifyStartAnalyzeProjects(nFiles: int): IDisposable = 
        client.CustomNotification("fsharp/startAnalyzeProjects", JsonValue.Number(decimal(nFiles)))
        let notifyEnd() = client.CustomNotification("fsharp/endAnalyzeProjects", JsonValue.Null)
        { new IDisposable with member this.Dispose() = notifyEnd() }
    let notifyAnalyzeProject(fsproj: FileInfo) = 
        client.CustomNotification("fsharp/analyzeProject", JsonValue.String(fsproj.Name))

    // Analyze a project and add it to the cache
    let rec analyzeProject(fsproj: FileInfo) = 
        dprintfn "Analyzing %s" fsproj.Name
        notifyAnalyzeProject(fsproj)
        match Projects.ProjectParser.crack(fsproj) with 
        | Error(e) -> analyzedByProjectFile.[fsproj.FullName] <- Error(e)
        | Ok(cracked) -> 
            // Ensure we've analyzed all dependencies
            // We'll need their target dlls to form FSharpProjectOptions
            for r in cracked.projectReferences do 
                ensureAnalyzed(r)
            // Populate source -> project cache
            for s in cracked.sources do 
                projectFileBySourceFile.[s.FullName] <- fsproj
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
                            | Error(_) -> () 
                            | Ok(cracked, _) -> yield "-r:" + cracked.target.FullName
                        // Reference packages
                        for r in cracked.packageReferences do 
                            yield "-r:" + r.FullName
                    |]
                ProjectFileName = fsproj.FullName 
                ReferencedProjects = 
                    [|
                        for r in cracked.projectReferences do 
                            match analyzedByProjectFile.[r.FullName] with 
                            | Error(_) -> () 
                            | Ok(cracked, projectOptions) -> yield cracked.target.FullName, projectOptions
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
            analyzedByProjectFile.[fsproj.FullName] <- Ok(cracked, options)
            // Log what we inferred
            // This is long but it's useful
            dprintfn "FSharpProjectOptions: "
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
    and ensureAnalyzed(fsproj: FileInfo) = 
        // TODO detect loop by caching set of `currentlyAnalyzing` projects
        if not (analyzedByProjectFile.ContainsKey(fsproj.FullName)) then 
            analyzeProject(fsproj)

    // Analyze multiple projects, with a progress bar
    let ensureAll(fsprojs: FileInfo list) = 
        use progress = notifyStartAnalyzeProjects(List.length(fsprojs))
        for f in fsprojs do ensureAnalyzed(f)

    // Re-analyze all projects
    let resetCaches() = 
        let projects = [for f in analyzedByProjectFile.Keys do yield FileInfo(f)]
        dprintfn "Re-analyze %A" [for p in projects do yield p.Name]
        analyzedByProjectFile.Clear()
        projectFileBySourceFile.Clear()
        ensureAll(projects)
    member this.AddWorkspaceRoot(root: DirectoryInfo): Async<unit> = 
        async {
            let all = root.EnumerateFiles("*.fsproj", SearchOption.AllDirectories)
            ensureAll(List.ofSeq(all))
        }
    member this.DeleteProjectFile(fsproj: FileInfo) = 
        analyzedByProjectFile.Remove(fsproj.FullName) |> ignore
        resetCaches()
    member this.UpdateProjectFile(fsproj: FileInfo) = 
        resetCaches()
    member this.NewProjectFile(fsproj: FileInfo) = 
        resetCaches()
        ensureAll([fsproj])
    member this.UpdateAssetsJson(assets: FileInfo) = 
        resetCaches()
    member this.FindProjectOptions(sourceFile: FileInfo): Result<FSharpProjectOptions, string> = 
        if projectFileBySourceFile.ContainsKey(sourceFile.FullName) then 
            let projectFile = projectFileBySourceFile.[sourceFile.FullName] 
            match analyzedByProjectFile.[projectFile.FullName] with 
            | Error(e) -> Error(e)
            | Ok(_, options) -> Ok(options)
        else Error(sprintf "No .fsproj file references %s" sourceFile.FullName)
    // All open projects, in dependency order
    // Ancestor projects come before projects that depend on them
    member this.OpenProjects: FSharpProjectOptions list = 
        let touched = new HashSet<String>()
        let result = new System.Collections.Generic.List<FSharpProjectOptions>()
        let rec walk(key: string) = 
            if touched.Add(key) then 
                match analyzedByProjectFile.[key] with 
                | Error(_) -> () 
                | Ok(_, options) -> 
                    for _, parent in options.ReferencedProjects do 
                        walk(parent.ProjectFileName)
                    result.Add(options)
        for key in analyzedByProjectFile.Keys do 
            walk(key)
        List.ofSeq(result)
    // All transitive dependencies of `projectFile`, in dependency order
    member this.TransitiveDeps(projectFile: FileInfo): FSharpProjectOptions list =
        let touched = new HashSet<String>()
        let result = new System.Collections.Generic.List<FSharpProjectOptions>()
        let rec walk(key: string) = 
            if touched.Add(key) then 
                match analyzedByProjectFile.[key] with 
                | Error(_) -> () 
                | Ok(_, options) -> 
                    for _, parent in options.ReferencedProjects do 
                        walk(parent.ProjectFileName)
                    result.Add(options)
        walk(projectFile.FullName)
        List.ofSeq(result)
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
