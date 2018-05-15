namespace Main 

open LSP.Log
open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Xml
open LSP.Json
open LSP.Json.JsonExtensions
open Microsoft.VisualBasic.CompilerServices
open Microsoft.FSharp.Compiler.SourceCodeServices
open ProjectParser

// Maintains caches of parsed versions of .fsproj files
type ProjectManager() = 
    // Index mapping .fs source files to .fsproj project files that reference them
    let projectFileBySourceFile = new Dictionary<String, FileInfo>()
    // Parse ?.fsproj and project.assets.json, but dont trace the dependencies yet
    let parseProject (fsproj: FileInfo) = 
        let assetsFile = FileInfo(Path.Combine [|fsproj.Directory.FullName; "obj"; "project.assets.json"|])
        let proj = parseFsProj fsproj
        let assets = parseAssets assetsFile
        // Register all sources as children of this .fsproj file
        match proj with 
        | Ok p -> 
            for f in p.compileInclude do
                projectFileBySourceFile.[f.FullName] <- fsproj
        | _ -> ()
        // If both .fsproj and project.assets.json are Ok, return result
        match proj, assets with 
        | Error e, _ -> Error e
        | _, Error e -> Error e
        | Ok proj, Ok assets -> Ok(proj, assets)
    // If ?.fsproj and project.assets.json can both be found and parsed, combine them into FSharpProjectOptions
    let analyzedByProjectFile = new Dictionary<String, Result<FSharpProjectOptions, string>>()
    // Find .dll corresponding to an .fsproj file 
    // For example, sample/IndirectDep/IndirectDep.fsproj corresponds to sample/IndirectDep/bin/Debug/netcoreapp2.0/IndirectDep.dll
    // See https://fsharp.github.io/FSharp.Compiler.Service/project.html#Analyzing-multiple-projects
    let projectDll (fsproj: FileInfo): string = 
        let bin = DirectoryInfo(Path.Combine(fsproj.Directory.FullName, "bin"))
        let name = fsproj.Name.Substring(0, fsproj.Name.Length - fsproj.Extension.Length) + ".dll" 
        // TODO this is pretty hacky
        // Does it actually matter if I find a real .dll? Can I just use bin/Debug/placeholder/___.dll?
        let list = [ if bin.Exists then
                        for target in bin.GetDirectories() do 
                            for platform in target.GetDirectories() do 
                                let file = Path.Combine(platform.FullName, name)
                                if File.Exists file then 
                                    yield file ]
        if list.Length > 0 then 
            list.[0] 
        else
            Path.Combine [|fsproj.Directory.FullName; "bin"; "placeholder"; name|]
    let ancestorDlls (refs: (string * FSharpProjectOptions)[]): string list = 
        let result = HashSet<string>()
        let rec traverse (refs: (string * FSharpProjectOptions)[]) = 
            for dll, options in refs do
                result.Add dll |> ignore
                traverse options.ReferencedProjects 
        traverse refs
        List.ofSeq result
    // For each successfully analyzed parent project, yield (dll, options)
    let rec projectReferences (proj: FsProj): (string * FSharpProjectOptions) list =
        let result = Dictionary<string, string * FSharpProjectOptions>()
        for parent in proj.projectReferenceInclude do 
            let found, maybeOptions = analyzedByProjectFile.TryGetValue(parent.FullName)
            if found then 
                match maybeOptions with 
                | Error e -> 
                    dprintfn "Project %s parent %s was excluded because analysis failed with error %s" proj.file.Name parent.Name e
                | Ok options -> 
                    // Add direct reference to parent 
                    let projectFile = FileInfo(options.ProjectFileName)
                    let projectDll = projectDll projectFile 
                    result.[options.ProjectFileName] <- (projectDll, options)
                    // Add indirect references
                    for (ancestorDll, ancestorOptions) in options.ReferencedProjects do 
                        result.[ancestorOptions.ProjectFileName] <- (ancestorDll, ancestorOptions)
            else dprintfn "Project %s parent %s was excluded because it was not analyzed" proj.file.Name parent.Name
        List.ofSeq result.Values
    and analyzeProject (proj: FsProj, assets: ProjectAssets): Result<FSharpProjectOptions, string> = 
        dprintfn "Analyzing %s" proj.file.FullName
        // Recursively ensure that all ancestors are analyzed and cached
        for parent in proj.projectReferenceInclude do 
            ensureAnalyzed parent
        let libraryDlls = findLibraryDlls assets
        let projectRefs = projectReferences proj |> Seq.toArray
        let projectDlls = ancestorDlls projectRefs
        dprintfn "Project %s" proj.file.FullName
        dprintfn "  Libraries:"
        for f in libraryDlls do 
            dprintfn "    %s" f.FullName
        dprintfn "  Projects:"
        for dll, options in projectRefs do 
            let relativeDll = Path.GetRelativePath(options.ProjectFileName, dll)
            dprintfn "    %s ~ %s" options.ProjectFileName relativeDll
        dprintfn "  Sources:"
        for f in proj.compileInclude do 
            dprintfn "    %s" f.FullName
        let options = 
            {
                ExtraProjectInfo = None 
                IsIncompleteTypeCheckEnvironment = false 
                LoadTime = proj.file.LastWriteTime
                OriginalLoadReferences = []
                OtherOptions = [|   yield "--noframework"
                                    // https://fsharp.github.io/FSharp.Compiler.Service/project.html#Analyzing-multiple-projects
                                    for f in projectDlls do
                                        yield "-r:" + f
                                    for f in libraryDlls do 
                                        yield "-r:" + f.FullName |]
                ProjectFileName = proj.file.FullName 
                ReferencedProjects = projectRefs
                SourceFiles = [| for f in proj.compileInclude do yield f.FullName |]
                Stamp = None 
                UnresolvedReferences = None 
                UseScriptResolutionRules = false
            }
        Ok options
    and ensureAnalyzed (fsproj: FileInfo) = 
        if not (analyzedByProjectFile.ContainsKey fsproj.FullName) then 
            analyzedByProjectFile.[fsproj.FullName] <- parseProject fsproj |> Result.bind analyzeProject
    // Remove project file and all its sources from caches
    // TODO invalidate descendents
    let rec invalidate (fsproj: FileInfo) = 
        if analyzedByProjectFile.ContainsKey fsproj.FullName then
            match analyzedByProjectFile.[fsproj.FullName] with 
            | Ok options -> 
                for source in options.SourceFiles do 
                    projectFileBySourceFile.Remove source |> ignore
            | _ -> () 
        analyzedByProjectFile.Remove fsproj.FullName |> ignore
    member this.AddWorkspaceRoot(root: DirectoryInfo) = 
        let all = root.EnumerateFiles("*.fsproj", SearchOption.AllDirectories)
        for f in all do 
            ensureAnalyzed f
    member this.DeleteProjectFile(fsproj: FileInfo) = 
        invalidate fsproj
    member this.UpdateProjectFile(fsproj: FileInfo) = 
        invalidate fsproj
        ensureAnalyzed fsproj
    member this.NewProjectFile(fsproj: FileInfo) = 
        invalidate fsproj
        ensureAnalyzed fsproj
    member this.UpdateAssetsJson(assets: FileInfo) = 
        for fsproj in assets.Directory.Parent.GetFiles("*.fsproj") do 
            this.UpdateProjectFile fsproj
    member this.FindProjectOptions(sourceFile: FileInfo): Result<FSharpProjectOptions, string> = 
        if projectFileBySourceFile.ContainsKey sourceFile.FullName then 
            let projectFile = projectFileBySourceFile.[sourceFile.FullName] 
            analyzedByProjectFile.[projectFile.FullName]
        else Error(sprintf "No .fsproj file references %s" sourceFile.FullName)
    member this.OpenProjects: FSharpProjectOptions list = 
        [ for each in analyzedByProjectFile.Values do 
            match each with 
            | Ok options -> yield options 
            | Error _ -> () ]