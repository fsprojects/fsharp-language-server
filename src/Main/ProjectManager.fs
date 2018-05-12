namespace Main 

open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Xml
open FSharp.Data
open FSharp.Data.JsonExtensions
open Microsoft.VisualBasic.CompilerServices
open Microsoft.FSharp.Compiler.SourceCodeServices
open ProjectParser

type private FoundProject = 
| GoodProject of FsProj * ProjectAssets
| ProjectFileError of string 
| ProjectAssetsError of string

// Maintains caches of parsed versions of .fsproj files
type ProjectManager() = 
    // Index mapping .fs source files to .fsproj project files that reference them
    let projectFileBySourceFile = new Dictionary<String, FileInfo>()
    // Once we find an .fsproj, parse it and the corresponding project.assets.json
    let parsedByProjectFile = new Dictionary<String, FoundProject>()
    // Parse ?.fsproj and project.assets.json, but dont trace the dependencies yet
    let ensureParsed (fsproj: FileInfo) = 
        if not(parsedByProjectFile.ContainsKey fsproj.FullName) then
            let assetsFile = FileInfo(Path.Combine [|fsproj.Directory.FullName; "obj"; "project.assets.json"|])
            let found, sources = 
                match parseFsProj fsproj, parseAssets assetsFile with 
                | Error e, _ -> ProjectFileError e, []
                | Ok proj, Error e -> ProjectAssetsError e, proj.compileInclude
                | Ok proj, Ok assets -> GoodProject(proj, assets), proj.compileInclude
            for f in sources do
                projectFileBySourceFile.[f.FullName] <- fsproj
            parsedByProjectFile.[fsproj.FullName] <- found 
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
    let printList (files: FileInfo list) (describe: string) =
        for f in files do 
            eprintfn "    %s" f.FullName
    let ancestorDlls (refs: (string * FSharpProjectOptions)[]): string list = 
        let result = HashSet<string>()
        let rec traverse (refs: (string * FSharpProjectOptions)[]) = 
            for dll, options in refs do
                result.Add dll |> ignore
                traverse options.ReferencedProjects 
        traverse refs
        List.ofSeq result
    let rec projectReferences (proj: FsProj): (string * FSharpProjectOptions)[] =   
        // For each successfully analyzed parent project, yield (dll, options)
        [| for p in proj.projectReferenceInclude do
                ensureAnalyzed p
                match analyzedByProjectFile.[p.FullName] with 
                | Ok options -> yield projectDll p, options 
                | _ -> () |]
    and analyzeProject (found: FoundProject): Result<FSharpProjectOptions, string> = 
        match found with 
        | ProjectFileError m -> Error m
        | ProjectAssetsError m -> Error m 
        | GoodProject(proj, assets) -> 
            eprintfn "Analyzing %s" proj.file.FullName
            let libraryDlls = findLibraryDlls assets
            let projectRefs = projectReferences proj 
            let projectDlls = ancestorDlls projectRefs
            eprintfn "Project %s" proj.file.FullName
            eprintfn "  Libraries:"
            printList libraryDlls "references"
            eprintfn "  Projects:"
            printList proj.projectReferenceInclude "projects"
            eprintfn "  Sources:"
            printList proj.compileInclude "sources"
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
        ensureParsed fsproj
        if not (analyzedByProjectFile.ContainsKey fsproj.FullName) then 
            analyzedByProjectFile.[fsproj.FullName] <- analyzeProject parsedByProjectFile.[fsproj.FullName]
    // Remove project file and all its sources from caches
    // TODO invalidate descendents
    let rec invalidate (fsproj: FileInfo) = 
        if parsedByProjectFile.ContainsKey fsproj.FullName then
            match parsedByProjectFile.[fsproj.FullName] with 
            | GoodProject(proj, assets) -> 
                for source in proj.compileInclude do 
                    projectFileBySourceFile.Remove source.FullName |> ignore
            | _ -> () 
        parsedByProjectFile.Remove fsproj.FullName |> ignore
        analyzedByProjectFile.Remove fsproj.FullName |> ignore
    member this.AddWorkspaceRoot(root: DirectoryInfo) = 
        let all = root.EnumerateFiles("*.fsproj", SearchOption.AllDirectories)
        for f in all do 
            ensureAnalyzed f
    member this.DeleteProjectFile(fsproj: FileInfo) = 
        invalidate fsproj
    member this.UpdateProjectFile(fsproj: FileInfo) = 
        invalidate fsproj
        ensureParsed fsproj
    member this.NewProjectFile(fsproj: FileInfo) = 
        invalidate fsproj
        ensureParsed fsproj
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