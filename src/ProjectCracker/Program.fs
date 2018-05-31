module ProjectCrackerMain

open System
open System.IO
open Buildalyzer

// This is just a utility for invoking Buildalyzer with more detailed output

let private buildFcs() = 
    let fsproj = "/Users/georgefraser/Documents/FSharp.Compiler.Service/fcs/FSharp.Compiler.Service/FSharp.Compiler.Service.fsproj"
    let manager = new AnalyzerManager()
    let analyzer = manager.GetProject(fsproj)
    // Print available targets
    for KeyValue(key, value) in analyzer.Project.Targets do 
        printfn "%s: %s" key value.DependsOnTargets
    // Invoke Restore, then build
    let project = analyzer.Load()
    let compile = project.CreateProjectInstance()
    let logger = Microsoft.Build.Logging.ConsoleLogger() :> Microsoft.Build.Framework.ILogger
    compile.Build("Restore", [logger]) |> ignore
    compile.Build("Build", [logger]) |> ignore
    // Dump all items
    let mutable t = "" 
    for i in compile.Items do 
        if i.ItemType <> t then 
            printfn "%s" i.ItemType 
            t <- i.ItemType 
        printfn "  %A" i

let private buildSomethingWellBehaved() = 
    let fsproj = "/Users/georgeThreadStaticAttributefraser/Documents/fsharp-language-server/sample/HasLocalDll/HasLocalDll.fsproj"
    let options = new AnalyzerManagerOptions()
    options.LogWriter <- Console.Error
    options.CleanBeforeCompile <- true
    let manager = new AnalyzerManager(options)
    let analyzer = manager.GetProject(fsproj)
    // Implicitly does analyzer.Load().CreateProjectInstance(), builds the "Compile" target
    let compile = analyzer.Compile()
    // Dump all items
    let mutable t = "" 
    for i in compile.Items do 
        if i.ItemType <> t then 
            printfn "%s" i.ItemType 
            t <- i.ItemType 
        printfn "  %A" i.EvaluatedInclude
    // let target = compile.GetItems("IntermediateAssembly")
    // let sourceFiles = compile.GetItems("Compile")
    // let projectReferences = compile.GetItems("ProjectReference")
    // let packageReferences = compile.GetItems("ReferencePath")
    // // Print important items
    // for t in target do printfn "Target: %s" t.EvaluatedInclude
    // printfn "Source Files: "
    // for r in sourceFiles do printfn "  %s" r.EvaluatedInclude
    // printfn "Project References: "
    // for r in projectReferences do printfn "  %s" r.EvaluatedInclude
    // printfn "Package References: "
    // for r in packageReferences do printfn "  %s" r.EvaluatedInclude

[<EntryPoint>]
let main(argv: array<string>): int = 
    0