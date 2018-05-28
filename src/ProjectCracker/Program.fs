module ProjectCracker 

open System
open System.IO

[<EntryPoint>]
let main(argv: array<string>): int = 
    let fsproj = FileInfo("/Users/georgefraser/Documents/fsharp-language-server/src/FSharpLanguageServer/FSharpLanguageServer.fsproj")
    use engine = new Microsoft.Build.Evaluation.ProjectCollection()
    let project = engine.LoadProject(fsproj.FullName)
    let instance = project.CreateProjectInstance()
    let targetPath = instance.GetPropertyValue("TargetPath")
    eprintfn "TargetPath: %s" targetPath
    0