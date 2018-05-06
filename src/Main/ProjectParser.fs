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

module ProjectParser = 
    type ParsedFsProj = {
        sources: list<FileInfo>
        projectReferences: list<FileInfo>
        references: list<FileInfo>
    }
    type Dependency = {
        ``type``: string 
        compile: list<string>
    }

    type Library = {
        path: string
    }

    type ProjectFrameworkDependency = {
        target: string 
        version: string 
        autoReferenced: bool 
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
        packageFolders: list<string>
        project: Project
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
    let private parseProjectFrameworkDependency (json: JsonValue): ProjectFrameworkDependency = 
        let autoReferenced = json.TryGetProperty("autoReferenced") |> Option.map (fun j -> j.AsBoolean())
        {
            target = json?target.AsString() 
            version = json?version.AsString()
            autoReferenced = defaultArg autoReferenced false
        }
    let private parseProjectFramework (project: JsonValue): ProjectFramework = 
        {
            dependencies = Map.ofSeq(seq {
                for dependency, info in project?dependencies.Properties do 
                    yield dependency, parseProjectFrameworkDependency info
            })
        }
    let private parseProject (project: JsonValue): Project = 
        {
            frameworks = Map.ofSeq(seq {
                for framework, info in project?frameworks.Properties do 
                    yield framework, parseProjectFramework info 
            })
        }
    let parseAssetsJson (text: string): ProjectAssets = 
        let json = JsonValue.Parse text
        {
            targets = parseTargets json?targets
            libraries = parseLibraries json?libraries
            packageFolders = keys json?packageFolders
            project = parseProject json?project
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
                    let normalizePath = Path.GetFullPath(absolutePath)
                    if File.Exists normalizePath then
                        yield FileInfo(normalizePath)
            } |> Seq.tryHead
        // Find a specific .dll file for a library with a version, for example "FSharp.Compiler.Service/22.0.3"
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
    let private parseFsProj (fsproj: FileInfo): XmlElement = 
        let text = File.ReadAllText fsproj.FullName
        let doc = XmlDocument()
        doc.LoadXml text 
        doc.DocumentElement
    // Parse fsproj and fsproj/../obj/project.assets.json
    let private parseBoth (path: FileInfo): ParsedFsProj = 
        let project = parseFsProj path
        // Find all <Compile Include=?> elements in fsproj
        let sources (fsproj: XmlNode): list<FileInfo> = 
            List.ofSeq(seq {
                for n in fsproj.SelectNodes "//Compile[@Include]" do 
                    let relativePath = n.Attributes.["Include"].Value |> fixPath
                    let absolutePath = Path.Combine(path.DirectoryName, relativePath)
                    let normalizePath = Path.GetFullPath(absolutePath)
                    yield FileInfo(normalizePath)
            })
        // Find all <ProjectReference Include=?> elements in fsproj
        let projectReferences (fsproj: XmlNode): list<FileInfo> = 
            List.ofSeq(seq {
                for n in fsproj.SelectNodes "//ProjectReference[@Include]" do 
                    let relativePath = n.Attributes.["Include"].Value |> fixPath
                    let absolutePath = Path.Combine(path.DirectoryName, relativePath)
                    let normalizePath = Path.GetFullPath(absolutePath)
                    yield FileInfo(normalizePath)
            })
        let assetsFile = Path.Combine(path.DirectoryName, "obj", "project.assets.json") |> FileInfo
        let rs = 
            if assetsFile.Exists then 
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
    let private printList (files: FileInfo list) (describe: string) =
        if List.length files > 10 then 
            eprintfn "    (%d %s)" (List.length files) describe 
        else 
            for f in files do 
                eprintfn "    %s" f.FullName
    // Find .dll corresponding to an .fsproj file 
    // For example, sample/IndirectDep/IndirectDep.fsproj corresponds to sample/IndirectDep/bin/Debug/netcoreapp2.0/IndirectDep.dll
    // See https://fsharp.github.io/FSharp.Compiler.Service/project.html#Analyzing-multiple-projects
    let private projectDll (fsproj: FileInfo): string = 
        let bin = DirectoryInfo(Path.Combine(fsproj.Directory.FullName, "bin"))
        let name = fsproj.Name.Substring(0, fsproj.Name.Length - fsproj.Extension.Length) + ".dll" 
        // TODO this is pretty hacky
        // Does it actually matter if I find a real .dll? Can I just use bin/Debug/placeholder/___.dll?
        let list = [ for target in bin.GetDirectories() do 
                        for platform in target.GetDirectories() do 
                            let file = Path.Combine(platform.FullName, name)
                            if File.Exists file then 
                                yield file ]
        if list.Length > 0 then 
            list.[0] 
        else
            Path.Combine [|fsproj.Directory.FullName; "bin"; "placeholder"; name|]
    // Traverse the tree of project references
    let private ancestors (fsproj: FileInfo): list<FileInfo> = 
        let all = List<FileInfo>()
        let rec traverse (fsproj: FileInfo) = 
            let head = parseBoth fsproj
            for r in head.projectReferences do 
                traverse r 
            all.Add(fsproj)
        let head = parseBoth fsproj 
        for r in head.projectReferences do 
            traverse r
        List.ofSeq all
    // Parse an .fsproj, looking at project.assets.json to find referenced .dlls
    let rec parseProjectOptions (fsproj: FileInfo): FSharpProjectOptions = 
        let c = parseBoth(fsproj)
        let ancestorProjects = ancestors fsproj
        eprintfn "Project %s" fsproj.FullName
        eprintfn "  References:"
        printList c.references "references"
        eprintfn "  Projects:"
        printList ancestorProjects "projects"
        eprintfn "  Sources:"
        printList c.sources "sources"
        // for f in ancestorProjects do
        //     eprintfn "-r:%O" (projectDll f)
        // for f in c.references do 
        //     eprintfn "-r:%O" f
        {
            ExtraProjectInfo = None 
            IsIncompleteTypeCheckEnvironment = false 
            LoadTime = DateTime.Now
            OriginalLoadReferences = []
            OtherOptions = [|   yield "--noframework" 
                                // https://fsharp.github.io/FSharp.Compiler.Service/project.html#Analyzing-multiple-projects
                                for f in ancestorProjects do
                                    yield sprintf "-r:%O" (projectDll f)
                                for f in c.references do 
                                    yield sprintf "-r:%O" f |]
            ProjectFileName = fsproj.FullName 
            ReferencedProjects = ancestorProjects |> List.map (fun f -> (projectDll f, parseProjectOptions f)) |> List.toArray
            SourceFiles = c.sources |> List.map (fun f -> f.FullName) |> List.toArray
            Stamp = None 
            UnresolvedReferences = None 
            UseScriptResolutionRules = false
        }