namespace Projects 

open LSP.Log
open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Xml
open System.Text.RegularExpressions
open LSP.Json
open LSP.Json.JsonExtensions
open LSP.Json.Ser
open Microsoft.VisualBasic.CompilerServices
open Microsoft.FSharp.Compiler.SourceCodeServices

module ProjectParser = 
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

    type FsProj = {
        file: FileInfo
        compileInclude: FileInfo list 
        projectReferenceInclude: FileInfo list
        referenceHintPath: FileInfo list
    }

    let private fixPath(path: string): string = 
        path.Replace('\\', Path.DirectorySeparatorChar)
        
    let private doParseAssetsJson = deserializerFactory<ProjectAssets> defaultJsonReadOptions
    // Exposed for testing
    let parseAssetsJson(jsonText: string) = 
        let jsonValue = JsonValue.Parse(jsonText)
        doParseAssetsJson jsonValue

    // Log messages once and then silence them
    let private alreadyLogged = System.Collections.Generic.HashSet<string>()
    let private logOnce(message: string): unit = 
        if not(alreadyLogged.Contains(message)) then 
            dprintfn "%s" message 
            alreadyLogged.Add(message) |> ignore
    // Substitute $(variables) in .fsproj files
    let private template = Regex(@"\$\((\w+)\)")
    let private substituteVariables(directory: DirectoryInfo, fsproj: string): string = 
        let doc = XmlDocument()
        doc.LoadXml(fsproj) 
        let variables = Dictionary<string, string>()
        let substituteMatch(m: Match) = 
            let name = m.Groups.[1].Value
            if variables.ContainsKey(name) then 
                logOnce(sprintf "Replace %s with %s" name variables.[name])
                variables.[name] 
            else 
                logOnce(sprintf "Leave %s because %s is not in %A" m.Value name variables)
                m.Value
        let substitute(text: string): string = 
            template.Replace(text, substituteMatch)
        variables.["MSBuildProjectDirectory"] <- directory.FullName
        for propGroup in doc.DocumentElement.SelectNodes "//PropertyGroup" do 
            dprintfn "Found %O" propGroup
            for prop in propGroup.ChildNodes do 
                dprintfn "  Child %O Name %s Value %s" prop prop.Name prop.InnerText
                variables.[prop.Name] <- substitute(prop.InnerText)
        substitute fsproj
    // Parse an .fsproj
    let parseFsProj(fsproj: FileInfo): Result<FsProj, string> = 
        try 
            let directory = fsproj.Directory
            let text = substituteVariables(directory, File.ReadAllText(fsproj.FullName))
            let doc = XmlDocument()
            doc.LoadXml text 
            // Find all <Compile Include=?> elements in fsproj
            let compileInclude = List.ofSeq(seq {
                for n in doc.DocumentElement.SelectNodes("//Compile[@Include]") do 
                    let relativePath = fixPath(n.Attributes.["Include"].Value)
                    let absolutePath = Path.Combine(directory.FullName, relativePath)
                    let normalizePath = Path.GetFullPath(absolutePath)
                    yield FileInfo(normalizePath)
            })
            // Find all <ProjectReference Include=?> elements in fsproj
            let projectReferenceInclude = List.ofSeq(seq {
                for n in doc.DocumentElement.SelectNodes "//ProjectReference[@Include]" do 
                    let relativePath = fixPath(n.Attributes.["Include"].Value)
                    let absolutePath = Path.Combine(directory.FullName, relativePath)
                    let normalizePath = Path.GetFullPath(absolutePath)
                    yield FileInfo(normalizePath)
            })
            // Find all <Reference><HintPath>?</></>
            let referenceHintPath = List.ofSeq(seq {
                for n in doc.DocumentElement.SelectNodes "//Reference/HintPath" do 
                    let relativePath = n.InnerText
                    let absolutePath = Path.Combine(directory.FullName, relativePath)
                    let normalizePath = Path.GetFullPath(absolutePath)
                    yield FileInfo(normalizePath)
            })
            {file=fsproj; compileInclude=compileInclude; projectReferenceInclude=projectReferenceInclude; referenceHintPath=referenceHintPath} |> Ok
        with e -> 
            Error(e.Message)
    // Parse a project.assets.json file
    let parseAssets(path: FileInfo): Result<ProjectAssets, string> = 
        if path.Exists then 
            let text = File.ReadAllText(path.FullName)
            let parsed = parseAssetsJson(text)
            Ok(parsed)
        else 
            let msg = sprintf "%s does not exist; maybe you need to build your project?" path.FullName
            Error(msg)
    // Find all dlls in project.assets.json
    let findLibraryDlls(assets: ProjectAssets): FileInfo list = 
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
            for KeyValue(frameworkName, framework) in assets.project.frameworks do 
                for KeyValue(dependencyName, dependency) in framework.dependencies do 
                    if dependency.autoReferenced = Some true then 
                        yield! Option.toList(lookupVersion(dependencyName))
        }
        // Identify which files are called out in the keys of in $.targets[*][dep/version].compile
        let compileFiles = seq {
            for KeyValue(targetName, libraryMap) in assets.targets do 
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