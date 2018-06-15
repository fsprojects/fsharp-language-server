namespace FSharpLanguageServer 

open LSP.Log
open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Xml
open FSharp.Data
open FSharp.Data.JsonExtensions
open LSP.Types
open Microsoft.FSharp.Compiler.SourceCodeServices
open ProjectCracker

type private ProjectOptions = 
| FsprojOptions of CrackedProject * FSharpProjectOptions 
| FsxOptions of FSharpProjectOptions * FSharpErrorInfo list 

/// Maintains caches of parsed versions of .fsprojOrFsx files
type ProjectManager(client: ILanguageClient, checker: FSharpChecker) = 
    // Index mapping .fs source files to .fsprojOrFsx project files that reference them
    let projectFileBySourceFile = new Dictionary<String, FileInfo>()
    // Cache the result of project cracking
    let analyzedByProjectFile = new Dictionary<String, ProjectOptions>()

    let printOptions(options: FSharpProjectOptions) = 
        // This is long but it's useful
        dprintfn "%s: " options.ProjectFileName
        dprintfn "  ProjectFileName: %A" options.ProjectFileName
        dprintfn "  SourceFiles: %A" options.SourceFiles
        dprintfn "  ReferencedProjects: %A" [for dll, _ in options.ReferencedProjects do yield dll]
        dprintfn "  OtherOptions: %A" options.OtherOptions
        dprintfn "  LoadTime: %A" options.LoadTime
        dprintfn "  ExtraProjectInfo: %A" options.ExtraProjectInfo
        dprintfn "  IsIncompleteTypeCheckEnvironment: %A" options.IsIncompleteTypeCheckEnvironment
        dprintfn "  OriginalLoadReferences: %A" options.OriginalLoadReferences
        dprintfn "  ExtraProjectInfo: %A" options.ExtraProjectInfo
        dprintfn "  Stamp: %A" options.Stamp
        dprintfn "  UnresolvedReferences: %A" options.UnresolvedReferences
        dprintfn "  UseScriptResolutionRules: %A" options.UseScriptResolutionRules

    let addToCache(key: FileInfo, value: ProjectOptions) = 
        // Populate project file -> options cache
        analyzedByProjectFile.[key.FullName] <- value 
        // Populate source -> project cache
        let sources = 
            match value with 
            | FsprojOptions(_, options) -> options.SourceFiles
            | FsxOptions(options, _) -> options.SourceFiles
        for s in sources do 
            projectFileBySourceFile.[s] <- key

    /// All transitive deps of an .fsproj file, including itself
    let transitiveDeps(projectFile: FileInfo) = 
        let touched = new HashSet<String>()
        let result = new System.Collections.Generic.List<FSharpProjectOptions>()
        let rec walk(key: string) = 
            if touched.Add(key) then 
                match analyzedByProjectFile.TryGetValue(key) with 
                | true, FsprojOptions(_, options) | true, FsxOptions(options, _) -> 
                    for _, parent in options.ReferencedProjects do 
                        walk(parent.ProjectFileName)
                    result.Add(options)
                | _, _ -> ()
        walk(projectFile.FullName)
        List.ofSeq(result)

    /// When was this .fsx, .fsproj or corresponding project.assets.json file modified?
    // TODO use checksum instead of time
    let lastModified(fsprojOrFsx: FileInfo) = 
        let assets = FileInfo(Path.Combine [| fsprojOrFsx.Directory.FullName; "obj"; "project.assets.json" |])
        if assets.Exists then 
            max fsprojOrFsx.LastWriteTime assets.LastWriteTime
        else 
            fsprojOrFsx.LastWriteTime

    /// Has this .fsproj or .fsx file or any of its transitive dependencies been modified?
    let rec needsUpdate(fsprojOrFsx: FileInfo) = 
        match analyzedByProjectFile.TryGetValue(fsprojOrFsx.FullName) with 
        | false, _ -> true 
        | _, FsprojOptions(_, options) -> 
            let deps = [for _, r in options.ReferencedProjects do yield FileInfo(r.ProjectFileName)]
            lastModified(fsprojOrFsx) > options.LoadTime || List.exists needsUpdate deps
        | _, FsxOptions(options, _) -> 
            lastModified(fsprojOrFsx) > options.LoadTime

    /// What projects need to be updated?
    let changedProjects() = 
        [for fileName in analyzedByProjectFile.Keys do 
            let file = FileInfo(fileName)
            if needsUpdate(file) then 
                yield file]

    /// Find base dlls
    /// Workaround of https://github.com/fsharp/FSharp.Compiler.Service/issues/847
    let dotNetFramework = 
        let dir = Path.GetDirectoryName(typeof<System.Object>.Assembly.Location)
        let relative = [
            "FSharp.Core.dll"
            "Microsoft.CSharp.dll"
            "Microsoft.VisualBasic.dll"
            "Microsoft.Win32.Primitives.dll"
            "System.AppContext.dll"
            "System.Buffers.dll"
            "System.Collections.Concurrent.dll"
            "System.Collections.Immutable.dll"
            "System.Collections.NonGeneric.dll"
            "System.Collections.Specialized.dll"
            "System.Collections.dll"
            "System.ComponentModel.Annotations.dll"
            "System.ComponentModel.Composition.dll"
            "System.ComponentModel.DataAnnotations.dll"
            "System.ComponentModel.EventBasedAsync.dll"
            "System.ComponentModel.Primitives.dll"
            "System.ComponentModel.TypeConverter.dll"
            "System.ComponentModel.dll"
            "System.Configuration.dll"
            "System.Console.dll"
            "System.Core.dll"
            "System.Data.Common.dll"
            "System.Data.dll"
            "System.Diagnostics.Contracts.dll"
            "System.Diagnostics.Debug.dll"
            "System.Diagnostics.DiagnosticSource.dll"
            "System.Diagnostics.FileVersionInfo.dll"
            "System.Diagnostics.Process.dll"
            "System.Diagnostics.StackTrace.dll"
            "System.Diagnostics.TextWriterTraceListener.dll"
            "System.Diagnostics.Tools.dll"
            "System.Diagnostics.TraceSource.dll"
            "System.Diagnostics.Tracing.dll"
            "System.Drawing.Primitives.dll"
            "System.Drawing.dll"
            "System.Dynamic.Runtime.dll"
            "System.Globalization.Calendars.dll"
            "System.Globalization.Extensions.dll"
            "System.Globalization.dll"
            "System.IO.Compression.FileSystem.dll"
            "System.IO.Compression.ZipFile.dll"
            "System.IO.Compression.dll"
            "System.IO.FileSystem.DriveInfo.dll"
            "System.IO.FileSystem.Primitives.dll"
            "System.IO.FileSystem.Watcher.dll"
            "System.IO.FileSystem.dll"
            "System.IO.IsolatedStorage.dll"
            "System.IO.MemoryMappedFiles.dll"
            "System.IO.Pipes.dll"
            "System.IO.UnmanagedMemoryStream.dll"
            "System.IO.dll"
            "System.Linq.Expressions.dll"
            "System.Linq.Parallel.dll"
            "System.Linq.Queryable.dll"
            "System.Linq.dll"
            "System.Net.Http.dll"
            "System.Net.HttpListener.dll"
            "System.Net.Mail.dll"
            "System.Net.NameResolution.dll"
            "System.Net.NetworkInformation.dll"
            "System.Net.Ping.dll"
            "System.Net.Primitives.dll"
            "System.Net.Requests.dll"
            "System.Net.Security.dll"
            "System.Net.ServicePoint.dll"
            "System.Net.Sockets.dll"
            "System.Net.WebClient.dll"
            "System.Net.WebHeaderCollection.dll"
            "System.Net.WebProxy.dll"
            "System.Net.WebSockets.Client.dll"
            "System.Net.WebSockets.dll"
            "System.Net.dll"
            "System.Numerics.Vectors.dll"
            "System.Numerics.dll"
            "System.ObjectModel.dll"
            "System.Reflection.DispatchProxy.dll"
            "System.Reflection.Emit.ILGeneration.dll"
            "System.Reflection.Emit.Lightweight.dll"
            "System.Reflection.Emit.dll"
            "System.Reflection.Extensions.dll"
            "System.Reflection.Metadata.dll"
            "System.Reflection.Primitives.dll"
            "System.Reflection.TypeExtensions.dll"
            "System.Reflection.dll"
            "System.Resources.Reader.dll"
            "System.Resources.ResourceManager.dll"
            "System.Resources.Writer.dll"
            "System.Runtime.CompilerServices.VisualC.dll"
            "System.Runtime.Extensions.dll"
            "System.Runtime.Handles.dll"
            "System.Runtime.InteropServices.RuntimeInformation.dll"
            "System.Runtime.InteropServices.WindowsRuntime.dll"
            "System.Runtime.InteropServices.dll"
            "System.Runtime.Loader.dll"
            "System.Runtime.Numerics.dll"
            "System.Runtime.Serialization.Formatters.dll"
            "System.Runtime.Serialization.Json.dll"
            "System.Runtime.Serialization.Primitives.dll"
            "System.Runtime.Serialization.Xml.dll"
            "System.Runtime.Serialization.dll"
            "System.Runtime.dll"
            "System.Security.Claims.dll"
            "System.Security.Cryptography.Algorithms.dll"
            "System.Security.Cryptography.Csp.dll"
            "System.Security.Cryptography.Encoding.dll"
            "System.Security.Cryptography.Primitives.dll"
            "System.Security.Cryptography.X509Certificates.dll"
            "System.Security.Principal.dll"
            "System.Security.SecureString.dll"
            "System.Security.dll"
            "System.ServiceModel.Web.dll"
            "System.ServiceProcess.dll"
            "System.Text.Encoding.Extensions.dll"
            "System.Text.Encoding.dll"
            "System.Text.RegularExpressions.dll"
            "System.Threading.Overlapped.dll"
            "System.Threading.Tasks.Dataflow.dll"
            "System.Threading.Tasks.Extensions.dll"
            "System.Threading.Tasks.Parallel.dll"
            "System.Threading.Tasks.dll"
            "System.Threading.Thread.dll"
            "System.Threading.ThreadPool.dll"
            "System.Threading.Timer.dll"
            "System.Threading.dll"
            "System.Transactions.Local.dll"
            "System.Transactions.dll"
            "System.ValueTuple.dll"
            "System.Web.HttpUtility.dll"
            "System.Web.dll"
            "System.Windows.dll"
            "System.Xml.Linq.dll"
            "System.Xml.ReaderWriter.dll"
            "System.Xml.Serialization.dll"
            "System.Xml.XDocument.dll"
            "System.Xml.XPath.XDocument.dll"
            "System.Xml.XPath.dll"
            "System.Xml.XmlDocument.dll"
            "System.Xml.XmlSerializer.dll"
            "System.Xml.dll"
            "System.dll"
            "WindowsBase.dll"
            "mscorlib.dll"
            "netstandard.dll"
        ]
        [ for d in relative do 
            let f = FileInfo(Path.Combine(dir, d))
            if f.Exists then 
                yield f
            else 
                dprintfn "Couldn't find %s in %s" d dir 
        ]

    /// Analyze a script file and add it to the cache
    let analyzeFsx(fsx: FileInfo) = 
        dprintfn "Creating project options for script %s" fsx.Name
        let source = File.ReadAllText(fsx.FullName)
        let inferred, errors = checker.GetProjectOptionsFromScript(fsx.FullName, source, fsx.LastWriteTime, assumeDotNetFramework=false) |> Async.RunSynchronously
        let combinedOtherOptions = [|
            for p in dotNetFramework do 
                    yield "-r:" + p.FullName
            for o in inferred.OtherOptions do 
                // If a dll is included by default, skip it
                let matchesName(f: FileInfo) = o.EndsWith(f.Name)
                let alreadyIncluded = List.exists matchesName dotNetFramework
                if not(alreadyIncluded) then
                    yield o 
        |]
        let options = {inferred with OtherOptions = combinedOtherOptions}
        printOptions(options)
        addToCache(fsx, FsxOptions(options, errors))

    let ensureAll(fs: FileInfo list) =
        use progress = new ProgressBar(fs.Length, sprintf "Analyze %d projects" fs.Length, client, fs.Length <= 1)
        /// Analyze a project and add it to the cache
        let rec analyzeFsproj(fsproj: FileInfo) = 
            dprintfn "Analyzing %s" fsproj.Name
            progress.Increment(fsproj)
            let cracked = ProjectCracker.crack(fsproj)
            // Ensure we've analyzed all dependencies
            // We'll need their target dlls to form FSharpProjectOptions
            for r in cracked.projectReferences do 
                ensureFsproj(r)
            // Convert to FSharpProjectOptions
            let options = {
                ExtraProjectInfo = None 
                IsIncompleteTypeCheckEnvironment = false 
                LoadTime = lastModified(fsproj)
                OriginalLoadReferences = []
                OtherOptions = 
                    [|
                        // Dotnet framework should be specified explicitly
                        yield "--noframework"
                        // Reference output of other projects
                        for r in cracked.projectReferences do 
                            match analyzedByProjectFile.[r.FullName] with 
                            | FsprojOptions(cracked, _) -> yield "-r:" + cracked.target.FullName
                            | _ -> ()
                        // Reference packages
                        for r in cracked.packageReferences do 
                            yield "-r:" + r.FullName
                    |]
                ProjectFileName = fsproj.FullName 
                ProjectId = None // This is apparently relevant to multi-targeting builds https://github.com/Microsoft/visualfsharp/pull/4918
                ReferencedProjects = 
                    [|
                        for r in cracked.projectReferences do 
                            match analyzedByProjectFile.[r.FullName] with 
                            | FsprojOptions(cracked, projectOptions) -> yield cracked.target.FullName, projectOptions
                            | _ -> ()
                    |]
                SourceFiles = 
                    [|
                        for f in cracked.sources do 
                            yield f.FullName
                    |]
                Stamp = None 
                UnresolvedReferences = None 
                UseScriptResolutionRules = false
            }
            // Cache inferred options
            addToCache(fsproj, FsprojOptions(cracked, options))
            // Log what we inferred
            printOptions(options)
        and ensureFsproj(fsproj: FileInfo) = 
            // TODO detect loop by caching set of `currentlyAnalyzing` projects
            if needsUpdate(fsproj) then 
                analyzeFsproj(fsproj)
        for f in fs do 
            if f.Name.EndsWith(".fsx") then 
                analyzeFsx(f)
            elif f.Name.EndsWith(".fsproj") then 
                ensureFsproj(f)
            else 
                dprintfn "Don't know how to analyze project %s" f.Name

    member this.AddWorkspaceRoot(root: DirectoryInfo): Async<unit> = 
        async {
            let all = [for f in root.EnumerateFiles("*.fs*", SearchOption.AllDirectories) do 
                        if f.Name.EndsWith(".fsx") || f.Name.EndsWith(".fsproj") then 
                            yield f]
            ensureAll(List.ofSeq(all))
        }
    member this.DeleteProjectFile(fsprojOrFsx: FileInfo) = 
        analyzedByProjectFile.Remove(fsprojOrFsx.FullName) |> ignore
        ensureAll(changedProjects())
    member this.NewProjectFile(fsprojOrFsx: FileInfo) = 
        ensureAll(fsprojOrFsx::changedProjects())
    member this.UpdateProjectFile(fsprojOrFsx: FileInfo) = 
        ensureAll(changedProjects())
    member this.UpdateAssetsJson(assets: FileInfo) = 
        ensureAll(changedProjects())
    member this.FindProjectOptions(sourceFile: FileInfo): Result<FSharpProjectOptions, Diagnostic list> = 
        if projectFileBySourceFile.ContainsKey(sourceFile.FullName) then 
            let projectFile = projectFileBySourceFile.[sourceFile.FullName] 
            match analyzedByProjectFile.[projectFile.FullName] with 
            | FsprojOptions(cracked, options) -> 
                match cracked.error with 
                | Some(message) -> Error([Conversions.errorAtTop(message)])
                | None -> Ok(options)
            | FsxOptions(options, []) -> Ok(options)
            | FsxOptions(_, errs) -> Error(Conversions.asDiagnostics(errs))
        else Error([Conversions.errorAtTop(sprintf "No .fsproj or .fsx file references %s" sourceFile.FullName)])
    /// All open projects, in dependency order
    /// Ancestor projects come before projects that depend on them
    member this.OpenProjects: FSharpProjectOptions list = 
        let touched = new HashSet<String>()
        let result = new System.Collections.Generic.List<FSharpProjectOptions>()
        let rec walk(key: string) = 
            if touched.Add(key) then 
                match analyzedByProjectFile.[key] with 
                | FsprojOptions(_, options) | FsxOptions(options, []) -> 
                    for _, parent in options.ReferencedProjects do 
                        walk(parent.ProjectFileName)
                    result.Add(options)
                | _ -> ()
        for key in analyzedByProjectFile.Keys do 
            walk(key)
        List.ofSeq(result)
    /// All transitive dependencies of `projectFile`, in dependency order
    member this.TransitiveDeps(projectFile: FileInfo): FSharpProjectOptions list =
        transitiveDeps(projectFile)
    /// Is `targetSourceFile` visible from `fromSourceFile`?
    member this.IsVisible(targetSourceFile: FileInfo, fromSourceFile: FileInfo) = 
        match this.FindProjectOptions(fromSourceFile) with 
        | Error(_) -> false 
        | Ok(fromProjectOptions) ->
            // If fromSourceFile is in the same project as targetSourceFile, check if iFrom comes after iTarget in the source file order
            if Array.contains targetSourceFile.FullName fromProjectOptions.SourceFiles then 
                let iTarget = Array.IndexOf(fromProjectOptions.SourceFiles, targetSourceFile.FullName)
                let iFrom = Array.IndexOf(fromProjectOptions.SourceFiles, fromSourceFile.FullName)
                iFrom >= iTarget
            // Otherwise, check if targetSourceFile is in the transitive dependencies of fromProjectOptions
            else
                let containsTarget(dependency: FSharpProjectOptions) = Array.contains targetSourceFile.FullName dependency.SourceFiles
                let deps = transitiveDeps(FileInfo(fromProjectOptions.ProjectFileName))
                List.exists containsTarget deps
