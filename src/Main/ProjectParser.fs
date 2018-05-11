namespace Main 

open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Xml
open System.Text.RegularExpressions
open FSharp.Data
open FSharp.Data.JsonExtensions
open Microsoft.VisualBasic.CompilerServices
open Microsoft.FSharp.Compiler.SourceCodeServices

module ProjectParser = 
    type ParsedFsProj = {
        sources: FileInfo list
        projectReferences: FileInfo list
        references: FileInfo list
    }
    type Dependency = {
        ``type``: string 
        compile: string list
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
        packageFolders: string list
        project: Project
    }

    type FsProj = {
        compileInclude: FileInfo list 
        projectReferenceInclude: FileInfo list
    }

    let private asString (json: JsonValue) = 
        json.AsString()
    let private asStringList (array: JsonValue): string list = 
        array.AsArray() |> Array.map asString |> List.ofArray
    let private asStringStringMap (json: JsonValue): Map<string, string> = 
        Map.ofSeq(seq {
            for k, v in json.Properties do
                yield k, v.AsString()
        })
    let private fixPath (path: string): string = 
        path.Replace('\\', Path.DirectorySeparatorChar)
    let keys (record: JsonValue): string list = 
        List.ofSeq(seq {
            for key, _ in record.Properties do 
                yield key 
        })
    let private parseDependency (info: JsonValue): Dependency = 
        {
            ``type`` = info?``type``.AsString()
            compile = info.TryGetProperty("compile") |> Option.map keys |> Option.defaultValue []
            dependencies = info.TryGetProperty("dependencies") |> Option.map asStringStringMap |> Option.defaultValue Map.empty
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
            ``type`` = library?``type``.AsString()
            path = library.TryGetProperty("path") |> Option.map asString
            files = library.TryGetProperty("files") |> Option.map asStringList |> Option.defaultValue []
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
    let private template = Regex(@"\$\((\w+)\)")
    let private substituteVariables (directory: DirectoryInfo) (fsproj: string): string = 
        let doc = XmlDocument()
        doc.LoadXml fsproj 
        let variables = Dictionary<string, string>()
        let substituteMatch (m: Match) = 
            let name = m.Groups.[1].Value
            if variables.ContainsKey(name) then 
                eprintfn "Replace %s with %s" name variables.[name]
                variables.[name] 
            else 
                eprintfn "Leave %s because %s is not in %A" m.Value name variables
                m.Value
        let substitute (text: string): string = 
            template.Replace(text, substituteMatch)
        variables.["MSBuildProjectDirectory"] <- directory.FullName
        for propGroup in doc.DocumentElement.SelectNodes "//PropertyGroup" do 
            eprintfn "Found %O" propGroup
            for prop in propGroup.ChildNodes do 
                eprintfn "  Child %O Name %s Value %s" prop prop.Name prop.InnerText
                variables.[prop.Name] <- substitute(prop.InnerText)
        substitute fsproj
    let parseFsProj (directory: DirectoryInfo) (fsproj: string): FsProj = 
        let text = substituteVariables directory fsproj
        let doc = XmlDocument()
        doc.LoadXml text 
        // Find all <Compile Include=?> elements in fsproj
        let compileInclude = List.ofSeq(seq {
            for n in doc.DocumentElement.SelectNodes "//Compile[@Include]" do 
                let relativePath = n.Attributes.["Include"].Value |> fixPath
                let absolutePath = Path.Combine(directory.FullName, relativePath)
                let normalizePath = Path.GetFullPath(absolutePath)
                yield FileInfo(normalizePath)
        })
        // Find all <ProjectReference Include=?> elements in fsproj
        let projectReferenceInclude = List.ofSeq(seq {
            for n in doc.DocumentElement.SelectNodes "//ProjectReference[@Include]" do 
                let relativePath = n.Attributes.["Include"].Value |> fixPath
                let absolutePath = Path.Combine(directory.FullName, relativePath)
                let normalizePath = Path.GetFullPath(absolutePath)
                yield FileInfo(normalizePath)
        })
        {compileInclude=compileInclude; projectReferenceInclude=projectReferenceInclude}
    let private parseAssets (path: FileInfo): ProjectAssets = 
        let text = File.ReadAllText path.FullName
        parseAssetsJson text
    // Log no-location messages once and then silence them
    let private alreadyLogged = System.Collections.Generic.HashSet<string>()
    let private logOnce (message: string): unit = 
        if not (alreadyLogged.Contains message) then 
            eprintfn "%s" message 
            alreadyLogged.Add(message) |> ignore
    // Find all dlls in project.assets.json
    let private references (assets: ProjectAssets): FileInfo list = 
        // Given a dependency name, for example FSharp.Core, lookup the version in $.libraries, for example FSharp.Core/4.3.4
        let lookupVersion (dependencyName: string) = 
            let mutable found: string option = None
            for KeyValue(dependencyVersion, library) in assets.libraries do 
                if dependencyVersion.StartsWith(dependencyName + "/") && found = None then 
                    found <- Some dependencyVersion
            found
        // Find all dependencies in $.project.frameworks with autoReferenced=true,
        // We will import the whole contents of these dependencies
        let autoReferenced = seq {
            for KeyValue(frameworkName, framework) in assets.project.frameworks do 
                for KeyValue(dependencyName, dependency) in framework.dependencies do 
                    if dependency.autoReferenced then 
                        yield! lookupVersion dependencyName |> Option.toList
        }
        // Identify which files are called out in the keys of in $.targets[*][dep/version].compile
        let compileFiles = seq {
            for KeyValue(targetName, libraryMap) in assets.targets do 
                for KeyValue(dependencyName, dependency) in libraryMap do 
                    for dll in dependency.compile do 
                        if dll.EndsWith ".dll" then 
                            yield (dependencyName, dll)
        }
        // Look up every autoReferenced dependency in $.libraries and include all DLLs 
        let autoReferencedFiles = seq {
            for dependency in autoReferenced do 
                if assets.libraries.ContainsKey(dependency) then 
                    for dll in assets.libraries.[dependency].files do 
                        if dll.EndsWith ".dll" then 
                            yield (dependency, dll)
                else logOnce(sprintf "Couldn't find auto-referenced dependency %s in libraries" dependency)
        }
        let allFiles = Seq.concat [ compileFiles; autoReferencedFiles ] |> Set.ofSeq
        // Look up each dependency in $.libraries[dep/version].files
        let libraryFile (dependency: string, dll: string) = seq {
            if assets.libraries.ContainsKey dependency then 
                let library = assets.libraries.[dependency]
                match library.path with 
                | None -> eprintfn "Skipping %s because no path in %A" dependency library
                | Some parentPath -> 
                    if List.contains dll library.files then 
                        yield Path.Combine(parentPath, dll)
                    else 
                        logOnce(sprintf "DLL %s is not in libraries[%s].files" dll dependency)
            else logOnce(sprintf "Dependency %s not in libraries" dependency)
        }
        let files = Seq.collect libraryFile allFiles
        // Find .dlls by checking each key of $.packageFolders
        let findAbsolutePath (relativePath: string): FileInfo option = 
            let mutable found: FileInfo option = None
            for packageFolder in assets.packageFolders do 
                let absolutePath = Path.Combine(packageFolder, relativePath)
                let normalizePath = Path.GetFullPath(absolutePath)
                if File.Exists normalizePath && found = None then
                    found <- Some(FileInfo(normalizePath))
            found
        Seq.map findAbsolutePath files |> Seq.collect Option.toList |> List.ofSeq
    // Parse fsproj and fsproj/../obj/project.assets.json
    let private parseBoth (path: FileInfo): ParsedFsProj = 
        let project = parseFsProj path.Directory (File.ReadAllText path.FullName)
        let assetsFile = Path.Combine(path.DirectoryName, "obj", "project.assets.json") |> FileInfo
        let rs = 
            if assetsFile.Exists then 
                let assets = parseAssets assetsFile
                references assets
            else 
                eprintfn "No assets file at %O" assetsFile
                []
        {
            sources = project.compileInclude
            projectReferences = project.projectReferenceInclude
            references = rs
        }
    let private printList (files: FileInfo list) (describe: string) =
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
    // Traverse the tree of project references
    let private ancestors (fsproj: FileInfo): FileInfo list = 
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
    let private projectOptionsCache = Dictionary<string, FSharpProjectOptions>()
    // Parse an .fsproj, looking at project.assets.json to find referenced .dlls
    let rec private doParseProjectOptions (fsproj: FileInfo): FSharpProjectOptions = 
        let c = parseBoth(fsproj)
        let ancestorProjects = ancestors fsproj
        eprintfn "Project %s" fsproj.FullName
        eprintfn "  References:"
        printList c.references "references"
        eprintfn "  Projects:"
        printList ancestorProjects "projects"
        eprintfn "  Sources:"
        printList c.sources "sources"
        {
            ExtraProjectInfo = None 
            IsIncompleteTypeCheckEnvironment = false 
            LoadTime = fsproj.LastWriteTime
            OriginalLoadReferences = []
            OtherOptions = [|   yield "--noframework"
                                // https://fsharp.github.io/FSharp.Compiler.Service/project.html#Analyzing-multiple-projects
                                for f in ancestorProjects do
                                    yield "-r:" + (projectDll f)
                                for f in c.references do 
                                    yield "-r:" + f.FullName |]
            ProjectFileName = fsproj.FullName 
            ReferencedProjects = ancestorProjects |> List.map (fun f -> (projectDll f, parseProjectOptions f)) |> List.toArray
            SourceFiles = c.sources |> List.map (fun f -> f.FullName) |> List.toArray
            Stamp = None 
            UnresolvedReferences = None 
            UseScriptResolutionRules = false
        }
    and parseProjectOptions (fsproj: FileInfo): FSharpProjectOptions = 
        let modified = fsproj.LastWriteTime
        if not (projectOptionsCache.ContainsKey fsproj.FullName) then 
            eprintfn "%s is not in the cache" fsproj.Name
            projectOptionsCache.[fsproj.FullName] <- doParseProjectOptions fsproj
        if projectOptionsCache.[fsproj.FullName].LoadTime.CompareTo(modified) < 0 then 
            eprintfn "%s has been modified" fsproj.Name
            projectOptionsCache.[fsproj.FullName] <- doParseProjectOptions fsproj
        projectOptionsCache.[fsproj.FullName]
    let openProjects () = 
        projectOptionsCache.Values |> List.ofSeq
    let invalidateProjectFile (uri: Uri) = 
        projectOptionsCache.Clear() // TODO make more selective
