module FSharpLanguageServer.ProjectManager.Types
open System
open System.IO
open LSP.Types
open FSharp.Compiler.CodeAnalysis
type ResolvedProject = {
    sources: FileInfo list
    options: FSharpProjectOptions
    target: FileInfo
    errors: Diagnostic list
}

type LazyProject = {
    file: FileInfo
    resolved: Lazy<ResolvedProject>
}