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
    let bySourceFile = new Dictionary<String, FSharpProjectOptions>()
    let byProjectFile = new Dictionary<String, FSharpProjectOptions>()
    let putProjectFile (fsproj: FileInfo) = 
        ProjectParser.invalidateProjectFile fsproj
        let projectOptions = ProjectParser.parseProjectOptions fsproj
        for f in projectOptions.SourceFiles do
            bySourceFile.[f] <- projectOptions
        byProjectFile.[fsproj.FullName] <- projectOptions
    member this.AddWorkspaceRoot(root: DirectoryInfo) = 
        for f in root.EnumerateFiles("*.fsproj", SearchOption.AllDirectories) do 
            putProjectFile f
    member this.DeleteProjectFile(fsproj: FileInfo) = 
        ProjectParser.invalidateProjectFile fsproj
        if byProjectFile.ContainsKey fsproj.FullName then 
            for f in byProjectFile.[fsproj.FullName].SourceFiles do 
                bySourceFile.Remove f |> ignore
            byProjectFile.Remove fsproj.FullName |> ignore
    member this.UpdateProjectFile(fsproj: FileInfo) = 
        putProjectFile(fsproj)
    member this.NewProjectFile(fsproj: FileInfo) = 
        putProjectFile(fsproj)
    member this.UpdateAssetsJson(assets: FileInfo) = 
        for fsproj in assets.Directory.Parent.GetFiles("*.fsproj") do 
            this.UpdateProjectFile fsproj
    member this.FindProjectOptions(sourceFile: FileInfo): FSharpProjectOptions option = 
        let file = sourceFile.FullName
        if bySourceFile.ContainsKey file then 
            bySourceFile.[file] |> Some 
        else 
            None
    member this.OpenProjects: FSharpProjectOptions list = 
        ProjectParser.openProjects()