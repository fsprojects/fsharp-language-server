namespace Main 

open LSP
open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Xml
open FSharp.Data
open FSharp.Data.JsonExtensions
open Microsoft.VisualBasic.CompilerServices
open Microsoft.FSharp.Compiler.SourceCodeServices

type CompilerOptions = {
    sources: list<FileInfo>
    projectReferences: list<FileInfo>
    references: list<FileInfo>
}

module ProjectManagerUtils = 
    type Dependency = {
        ``type``: string 
        compile: list<string>
    }

    type Library = {
        path: string
    }

    type ProjectAssets = {
        targets: Map<string, Map<string, Dependency>>
        libraries: Map<string, Library>
        packageFolders: list<string>
    }

    let private fixPath (path: string): string = 
        path.Replace('\\', Path.DirectorySeparatorChar)
    let keys (record: JsonValue): list<string> = 
        List.ofSeq(seq {
            for key, _ in record.Properties do 
                yield key 
        })
    let private parseDependency (info: JsonValue): Dependency = 
        let compile = info.TryGetProperty("compile") |> Option.map keys
        {
            ``type`` = info?``type``.AsString()
            compile = defaultArg compile []
        }
    let private parseDependencies (dependencies: JsonValue): Map<string, Dependency> = 
        Map.ofSeq(seq {
            for name, info in dependencies.Properties do 
                yield name, parseDependency info
        })
    let private parseTargets (targets: JsonValue): Map<string, Map<string, Dependency>> = 
        Map.ofSeq(seq {
            for target, dependencies in targets.Properties do 
                yield target, parseDependencies dependencies
        })
    let private parseLibrary (library: JsonValue): Library = 
        {
            path = library?path.AsString()
        }
    let private parseLibraries (libraries: JsonValue): Map<string, Library> = 
        Map.ofSeq(seq {
            for dependency, info in libraries.Properties do 
                yield dependency, parseLibrary info
        })
    let parseAssetsJson (text: string): ProjectAssets = 
        let json = JsonValue.Parse text
        {
            targets = parseTargets json?targets
            libraries = parseLibraries json?libraries
            packageFolders = keys json?packageFolders
        }
    let private parseAssets (path: FileInfo): ProjectAssets = 
        let text = File.ReadAllText path.FullName
        parseAssetsJson text
    // Find all dlls in project.assets.json
    let private references (assets: ProjectAssets): list<FileInfo> = 
        let resolveInPackageFolders (dependencyPath: string): option<FileInfo> = 
            seq {
                for packageFolder in assets.packageFolders do 
                    let absolutePath = Path.Combine(packageFolder, dependencyPath)
                    if File.Exists absolutePath then
                        yield FileInfo(absolutePath)
            } |> Seq.tryHead
        let resolveInLibrary (library: string) (dll: string): option<FileInfo> = 
            let libraryPath = assets.libraries.[library].path
            let dependencyPath = Path.Combine(libraryPath, dll) |> fixPath 
            resolveInPackageFolders dependencyPath
        List.ofSeq(seq {
            for target in assets.targets do 
                for dependency in target.Value do 
                    if dependency.Value.``type`` = "package" && Map.containsKey dependency.Key assets.libraries then 
                        for dll in dependency.Value.compile do 
                            let resolved = resolveInLibrary dependency.Key dll
                            if resolved.IsSome then 
                                yield resolved.Value 
                            else 
                                let packageFolders = String.concat ", " assets.packageFolders
                                eprintfn "Couldn't find %s in %s" dll packageFolders
        })
    // Parse fsproj
    let private parseProject (fsproj: FileInfo): XmlElement = 
        let text = File.ReadAllText fsproj.FullName
        let doc = XmlDocument()
        doc.LoadXml text 
        doc.DocumentElement
    // Parse fsproj and fsproj/../obj/project.assets.json
    let parseBoth (path: FileInfo): CompilerOptions = 
        let project = parseProject path
        // Find all <Compile Include=?> elements in fsproj
        let sources (fsproj: XmlNode): list<FileInfo> = 
            List.ofSeq(seq {
                for n in fsproj.SelectNodes "//Compile[@Include]" do 
                    let relativePath = n.Attributes.["Include"].Value |> fixPath
                    let absolutePath = Path.Combine(path.DirectoryName, relativePath)
                    yield FileInfo(absolutePath)
            })
        // Find all <ProjectReference Include=?> elements in fsproj
        let projectReferences (fsproj: XmlNode): list<FileInfo> = 
            List.ofSeq(seq {
                for n in fsproj.SelectNodes "//ProjectReference[@Include]" do 
                    let relativePath = n.Attributes.["Include"].Value |> fixPath
                    let absolutePath = Path.Combine(path.DirectoryName, relativePath)
                    yield FileInfo(absolutePath)
            })
        let assetsFile = Path.Combine(path.DirectoryName, "obj", "project.assets.json") |> FileInfo
        let rs = 
            if assetsFile.Exists then 
                eprintfn "Found %O" assetsFile
                let assets = parseAssets assetsFile
                references assets
            else 
                eprintfn "No assets file at %O" assetsFile
                []
        {
            sources = sources project 
            projectReferences = projectReferences project
            references = rs
        }
    let rec parseProjectOptions (fsproj: FileInfo): FSharpProjectOptions = 
        let c = parseBoth(fsproj)
        let options = 
            [|  yield "--noframework" 
                for r in c.references do 
                    yield sprintf "-r:%O" r |]
        eprintfn "fsc %s" (String.concat " " options)
        {
            ExtraProjectInfo = None 
            IsIncompleteTypeCheckEnvironment = false 
            LoadTime = DateTime.Now
            OriginalLoadReferences = []
            OtherOptions = options
            ProjectFileName = fsproj.FullName 
            ReferencedProjects = c.projectReferences |> List.map (fun f -> (f.FullName, parseProjectOptions f)) |> List.toArray
            SourceFiles = c.sources |> List.map (fun f -> f.FullName) |> List.toArray
            Stamp = None 
            UnresolvedReferences = None 
            UseScriptResolutionRules = false
        }

open ProjectManagerUtils

type ProjectManager() = 
    // Scan the parent directories looking for a file *.fsproj
    let findProjectFileInParents (sourceFile: FileInfo): option<FileInfo> = 
        seq {
            let mutable dir = sourceFile.Directory
            while dir <> dir.Root do 
                for proj in dir.GetFiles("*.fsproj") do 
                    yield proj
                dir <- dir.Parent
        } |> Seq.tryHead
    let parseCache = new Dictionary<String, FSharpProjectOptions>()
    let projectFileCache = new Dictionary<String, FileInfo>()
    let cachedParse (projectFile: FileInfo): FSharpProjectOptions = 
        if not (parseCache.ContainsKey projectFile.FullName) then 
            eprintfn "Project file %O has not been parsed" projectFile 
            parseCache.[projectFile.FullName] <- parseProjectOptions projectFile
        parseCache.[projectFile.FullName]
    let cachedProjectFile (sourceFile: FileInfo): option<FileInfo> = 
        let sourceDir = sourceFile.Directory 
        if projectFileCache.ContainsKey(sourceDir.FullName) then 
            Some (projectFileCache.[sourceDir.FullName])
        else 
            match findProjectFileInParents sourceFile with 
            | None -> 
                eprintfn "No project file for %s" sourceFile.Name
                None
            | Some projectFile -> 
                eprintfn "Found project file %s for %s" projectFile.FullName sourceFile.Name
                projectFileCache.[sourceDir.FullName] <- projectFile 
                Some projectFile 
    member this.UpdateProjectFile(project: Uri): unit = 
        let file = FileInfo(project.AbsolutePath)
        eprintfn "Clear project files caches for %O" project
        // TODO make this more selective
        parseCache.Clear() 
        projectFileCache.Clear()
    member this.FindProjectOptions(sourceFile: Uri): option<FSharpProjectOptions> = 
        let file = FileInfo(sourceFile.AbsolutePath)
        let project = cachedProjectFile file
        Option.map cachedParse project