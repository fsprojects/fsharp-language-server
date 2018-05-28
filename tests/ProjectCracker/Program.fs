module ProjectCracker 

open System
open System.IO
open Buildalyzer

// This is just a utility for invoking Buildalyzer with more detailed output

[<EntryPoint>]
let main(argv: array<string>): int = 
    let csproj = "/Users/georgefraser/Documents/Buildalyzer/src/Buildalyzer/Buildalyzer.csproj"
    let fsproj = "/Users/georgefraser/Documents/fsharp-language-server/sample/HasLocalDll/HasLocalDll.fsproj"

    let options = new AnalyzerManagerOptions()
    options.LogWriter <- Console.Error
    options.CleanBeforeCompile <- false
    let manager = new AnalyzerManager(options)
    let analyzer = manager.GetProject(fsproj)
    let compile = analyzer.Compile()

    let target = compile.GetItems("IntermediateAssembly")
    let sourceFiles = compile.GetItems("Compile")
    let projectReferences = compile.GetItems("ProjectReference")
    let packageReferences = compile.GetItems("ReferencePath")

    let mutable t = "" 
    for i in compile.Items do 
        if i.ItemType <> t then 
            printfn "%s" i.ItemType 
            t <- i.ItemType 
        printfn "  %A" i

    for t in target do printfn "Target: %s" t.EvaluatedInclude
    printfn "Source Files: "
    for r in sourceFiles do printfn "  %s" r.EvaluatedInclude
    printfn "Project References: "
    for r in projectReferences do printfn "  %s" r.EvaluatedInclude
    printfn "Package References: "
    for r in packageReferences do printfn "  %s" r.EvaluatedInclude
    0