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

// Maintains caches of parsed versions of .fsproj files
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
    let projectFileCache = new Dictionary<String, FileInfo>()
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
    member this.FindProjectFile(sourceFile: Uri): option<FileInfo> = 
        let file = FileInfo(sourceFile.AbsolutePath)
        cachedProjectFile file
    member this.FindProjectOptions(fsproj: FileInfo): FSharpProjectOptions = 
        ProjectParser.parseProjectOptions fsproj
    member this.OpenProjects: FSharpProjectOptions list = 
        ProjectParser.openProjects()