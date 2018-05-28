namespace Projects 

open LSP.Log
open System
open System.IO
open Buildalyzer

module ProjectParser = 
    type CrackedProject = {
        // ?.fsproj file that was cracked
        fsproj: FileInfo 
        // ?.dll file built by this .fsproj file
        // Dependent projects will reference this dll in fscArgs, like "-r:?.dll"
        target: FileInfo
        // List of source files
        // These are fsc args, but presented separately because that's how FSharpProjectOptions wants them
        sources: FileInfo list
        // .fsproj files on references projects 
        projectReferences: FileInfo list 
        // .dlls
        packageReferences: FileInfo list 
    }

    // Extract the key information we need to form FSharpProjectOptions from a .fsproj file
    // Uses dotnet-proj-info and an external process
    let crack(fsproj: FileInfo): Result<CrackedProject, string> = 
        // Create an msbuild instance
        let options = new AnalyzerManagerOptions()
        options.LogWriter <- !diagnosticsLog // TODO this doesn't follow ref changes
        options.CleanBeforeCompile <- false
        let manager = new AnalyzerManager(options) // TODO consider keeping this alive between invocations
        // Compile the project
        try 
            let analyzer = manager.GetProject(fsproj.FullName)
            let compile = analyzer.Compile()
            // Get key items from build output
            let targets = compile.GetItems("IntermediateAssembly")
            let sources = compile.GetItems("Compile")
            let projectReferences = compile.GetItems("ProjectReference")
            let packageReferences = compile.GetItems("ReferencePath")
            let files(items: Microsoft.Build.Execution.ProjectItemInstance seq) = 
                [ for i in items do 
                    let relativePath = i.EvaluatedInclude
                    let absolutePath = Path.Combine(fsproj.DirectoryName, relativePath)
                    let normalizePath = Path.GetFullPath(absolutePath)
                    yield FileInfo(normalizePath) ]
            let [target] = files(targets) // TODO this might not be true in error cases
            Ok({
                fsproj=fsproj
                target=target
                sources=files(sources)
                projectReferences=files(projectReferences)
                packageReferences=files(packageReferences)
            })
        with e -> Error(e.Message)