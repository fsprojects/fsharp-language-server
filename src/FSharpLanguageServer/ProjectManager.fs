namespace FSharpLanguageServer

open LSP.Log
open System
open System.IO
open System.Collections.Generic
open System.Net
open System.Text.RegularExpressions
open System.Xml
open FSharp.Data
open FSharp.Data.JsonExtensions
open LSP.Types
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.Text
open ProjectCracker

type private ResolvedProject = {
    sources: FileInfo list
    options: FSharpProjectOptions
    target: FileInfo
    errors: Diagnostic list
}

type private LazyProject = {
    file: FileInfo
    resolved: Lazy<ResolvedProject>
}

type private ProjectCache() =
    let knownProjects = new Dictionary<String, LazyProject>()

    member this.Invalidate(fsprojOrFsx: FileInfo) =
        knownProjects.Remove(fsprojOrFsx.FullName) |> ignore
    member this.Get(fsprojOrFsx: FileInfo, analyzeLater: FileInfo -> LazyProject): LazyProject =
        if not(knownProjects.ContainsKey(fsprojOrFsx.FullName)) then
            knownProjects.Add(fsprojOrFsx.FullName, analyzeLater(fsprojOrFsx))
        knownProjects.[fsprojOrFsx.FullName]

/// Maintains caches of parsed versions of .fsprojOrFsx files
type ProjectManager(checker: FSharpChecker) =
    /// Remember what .fsproj files are referenced by .sln files
    /// Keys are full paths to .sln files
    /// Values are lists of .fsproj files referenced by the .sln file
    let knownSolutions = new Dictionary<String, list<FileInfo>>()
    /// Remember what .fsproj and .fsx files are present
    let knownProjects = new HashSet<String>()
    //// Cache expensive analyze operations
    let cache = ProjectCache()

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

    /// When was this .fsx, .fsproj or corresponding project.assets.json file modified?
    // TODO use checksum instead of time
    let lastModified(fsprojOrFsx: FileInfo) =
        let assets = FileInfo(Path.Combine [| fsprojOrFsx.Directory.FullName; "obj"; "project.assets.json" |])
        if assets.Exists then
            max fsprojOrFsx.LastWriteTime assets.LastWriteTime
        else
            fsprojOrFsx.LastWriteTime

    /// Find any .fsproj files associated with a project.assets.json
    let projectFileForAssets(assetsJson: FileInfo) =
        let dir = assetsJson.Directory.Parent
        dir.GetFiles("*.fsproj")

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

    /// Analyze a .fsx or .fsproj file
    let rec analyzeLater(fsprojOrFsx: FileInfo): LazyProject =
        /// Analyze a script file
        let analyzeFsx(fsx: FileInfo) =
            dprintfn "Creating project options for script %s" fsx.Name
            let source = SourceText.ofString(File.ReadAllText(fsx.FullName))
            let inferred, errors = checker.GetProjectOptionsFromScript(fsx.FullName, source, loadedTimeStamp=fsx.LastWriteTime, assumeDotNetFramework=false) |> Async.RunSynchronously
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
            {
                sources=[for f in inferred.SourceFiles do yield FileInfo(f)]
                options=options
                target=FileInfo("NoOutputForFsx")
                errors=Conversions.asDiagnostics(errors)
            }
        /// Analyze a project
        let analyzeFsproj(fsproj: FileInfo) =
            dprintfn "Analyzing %s" fsproj.Name
            let cracked = ProjectCracker.crack(fsproj)
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
                            let options = cache.Get(r, analyzeLater)
                            yield "-r:" + options.resolved.Value.target.FullName
                        // Reference target .dll for .csproj proejcts
                        for r in cracked.otherProjectReferences do
                            yield "-r:" + r.FullName
                        // Reference packages
                        for r in cracked.packageReferences do
                            yield "-r:" + r.FullName
                        // Direct dll references
                        for r in cracked.directReferences do
                            yield "-r:" + r.FullName
                    |]
                ProjectFileName = fsproj.FullName
                ProjectId = None // This is apparently relevant to multi-targeting builds https://github.com/Microsoft/visualfsharp/pull/4918
                ReferencedProjects =
                    [|
                        for r in cracked.projectReferences do
                            let options = cache.Get(r, analyzeLater)
                            yield options.resolved.Value.target.FullName, options.resolved.Value.options
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
            // Log what we inferred
            printOptions(options)
            {
                sources=cracked.sources
                options=options
                target=cracked.target
                errors=match cracked.error with None -> [] | Some(e) -> [Conversions.errorAtTop(e)]
            }
        // Direct to analyzeFsx or analyzeFsproj, depending on type
        if fsprojOrFsx.Name.EndsWith(".fsx") then
            {file=fsprojOrFsx; resolved=lazy(analyzeFsx(fsprojOrFsx))}
        elif fsprojOrFsx.Name.EndsWith(".fsproj") then
            {file=fsprojOrFsx; resolved=lazy(analyzeFsproj(fsprojOrFsx))}
        else
            raise(Exception(sprintf "Don't know how to analyze project %s" fsprojOrFsx.Name))

    /// Invalidate all descendents of a modified .fsproj or .fsx file
    let invalidateDescendents(fsprojOrFsx: FileInfo) =
        cache.Invalidate(fsprojOrFsx)
        for fileName in knownProjects do
            let file = FileInfo(fileName)
            let project = cache.Get(file, analyzeLater)
            if project.resolved.IsValueCreated then
                for _, ancestor in project.resolved.Value.options.ReferencedProjects do
                    if ancestor.ProjectFileName = fsprojOrFsx.FullName then
                        dprintfn "%s has been invalidated by changes to %s" ancestor.ProjectFileName fsprojOrFsx.Name
                        cache.Invalidate(FileInfo(ancestor.ProjectFileName))

    /// All transitive deps of anproject, including itself
    let transitiveDeps(fsprojOrFsx: FileInfo) =
        let touched = new HashSet<String>()
        let result = new List<FSharpProjectOptions>()
        let rec walk(options: FSharpProjectOptions) =
            if touched.Add(options.ProjectFileName) then
                for _, parent in options.ReferencedProjects do
                    walk(parent)
                result.Add(options)
        let root = cache.Get(fsprojOrFsx, analyzeLater)
        walk(root.resolved.Value.options)
        List.ofSeq(result)

    /// Find all .fsproj files referenced by a .sln file
    let slnProjectReferences (sln: FileInfo): list<FileInfo> =
        // From https://github.com/OmniSharp/omnisharp-roslyn/blob/master/src/OmniSharp.MSBuild/SolutionParsing/ProjectBlock.cs
        let projectHeader =
                Regex(
                    "^" // Beginning of line
                    + "Project\\(\"(?<PROJECTTYPEGUID>.*)\"\\)"
                    + "\\s*=\\s*" // Any amount of whitespace plus "=" plus any amount of whitespace
                    + "\"(?<PROJECTNAME>.*)\""
                    + "\\s*,\\s*" // Any amount of whitespace plus "," plus any amount of whitespace
                    + "\"(?<RELATIVEPATH>.*)\""
                    + "\\s*,\\s*" // Any amount of whitespace plus "," plus any amount of whitespace
                    + "\"(?<PROJECTGUID>.*)\""
                    + "$", // End-of-line
                    RegexOptions.Compiled
                )
        [ for line in File.ReadLines sln.FullName do
            let m = projectHeader.Match(line)
            if m.Success then
                let relativePath = m.Groups.["RELATIVEPATH"].Value.Trim();
                if relativePath.EndsWith(".fsproj") then
                    let path = Path.Combine(sln.Directory.FullName, relativePath)
                    let normalize = Path.GetFullPath(path)
                    yield FileInfo(normalize) ]
    member this.AddWorkspaceRoot(root: DirectoryInfo): Async<unit> =
        async {
            for f in root.EnumerateFiles("*.*", SearchOption.AllDirectories) do
                if f.Name.EndsWith(".fsx") || f.Name.EndsWith(".fsproj") then
                    knownProjects.Add(f.FullName) |> ignore
                else if f.Name.EndsWith(".sln") then
                    knownSolutions.[f.FullName] <- slnProjectReferences(f)
        }
    member this.DeleteProjectFile(fsprojOrFsx: FileInfo) =
        knownProjects.Remove(fsprojOrFsx.FullName) |> ignore
        cache.Invalidate(fsprojOrFsx) |> ignore
        invalidateDescendents(fsprojOrFsx)
    member this.NewProjectFile(fsprojOrFsx: FileInfo) =
        knownProjects.Add(fsprojOrFsx.FullName) |> ignore
        invalidateDescendents(fsprojOrFsx)
    member this.UpdateProjectFile(fsprojOrFsx: FileInfo) =
        invalidateDescendents(fsprojOrFsx)
    member this.DeleteSlnFile(sln: FileInfo) =
        knownSolutions.Remove(sln.FullName) |> ignore
    member this.UpdateSlnFile(sln: FileInfo) =
        knownSolutions.[sln.FullName] <- slnProjectReferences(sln)
    member this.UpdateAssetsJson(assets: FileInfo) =
        for fsproj in projectFileForAssets(assets) do invalidateDescendents(fsproj)
    member this.FindProjectOptions(sourceFile: FileInfo): Result<FSharpProjectOptions, Diagnostic list> =
        let isSourceFile(f: FileInfo) = f.FullName = sourceFile.FullName
        // Does `p` contain a reference to `sourceFile`?
        let isMatch(p: ResolvedProject) = List.exists isSourceFile p.sources
        // Check if the text of `p` contains the name of `sourceFile` without cracking it
        let isPotentialMatch(p: LazyProject) =
            let containsFileName(line: string) = line.Contains(sourceFile.Name)
            let lines = File.ReadAllLines(p.file.FullName)
            Array.exists containsFileName lines
        let isCracked(p: LazyProject) = p.resolved.IsValueCreated
        let knownProjectsList = [for f in knownProjects do yield cache.Get(FileInfo(f), analyzeLater)]
        let alreadyCracked, notYetCracked = List.partition isCracked knownProjectsList
        let isReferencedBySln(fsproj: LazyProject) =
            seq {
                for KeyValue(sln, fsprojs) in knownSolutions do
                    for f in fsprojs do
                        if fsproj.file.FullName = f.FullName then
                            dprintfn "%s is referenced by %s" f.Name sln
                            yield sln
            } |> Seq.isEmpty |> not
        let referencedProjects, orphanProjects = List.partition isReferencedBySln notYetCracked
        let crackLazily = seq {
            // If file is an .fsx, return itself
            if sourceFile.Name.EndsWith(".fsx") then
                yield cache.Get(sourceFile, analyzeLater)
            // First, look at all projects that have *already* been cracked
            for options in alreadyCracked do
                if isMatch(options.resolved.Value) then
                    yield options
            // If that doesn't work, check other .fsproj files
            dprintfn "No cracked project references %s, looking at uncracked projects..." sourceFile.Name
            // Prioritize .fsproj files that are referenced by .sln files
            for options in referencedProjects@orphanProjects do
                // Only parse projects that contain the simple name of `sourceFile`
                if isPotentialMatch(options) then
                    dprintfn "The text of %s contains the string '%s', cracking" options.file.Name sourceFile.Name
                    if isMatch(options.resolved.Value) then
                        yield options
        }
        match Seq.tryHead crackLazily with
        | None -> Error([Conversions.errorAtTop(sprintf "No .fsproj or .fsx file references %s" sourceFile.FullName)])
        | Some(options) ->
            let cracked = options.resolved.Value
            if cracked.errors.IsEmpty then
                Ok(cracked.options)
            else
                Error(cracked.errors)
    /// All open projects, in dependency order.
    /// Ancestor projects come before projects that depend on them.
    member this.OpenProjects: FSharpProjectOptions list =
        let touched = new HashSet<String>()
        let result = new List<FSharpProjectOptions>()
        let rec walk(options: FSharpProjectOptions) =
            if touched.Add(options.ProjectFileName) then
                for _, parent in options.ReferencedProjects do
                    walk(parent)
                result.Add(options)
        for f in knownProjects do
            let project = cache.Get(FileInfo(f), analyzeLater)
            if project.resolved.IsValueCreated then
                walk(project.resolved.Value.options)
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
