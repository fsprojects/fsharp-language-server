namespace FSharpLanguageServer

open System
open System.IO
open LSP
open LSP.Types
open FSharp.Data

/// When we check a long series of files, create a progress bar
type ProgressBar(nFiles: int, title: string, client: ILanguageClient, ?hide: bool) = 
    let hide = defaultArg hide false
    let message = JsonValue.Record [|   "title", JsonValue.String(title)
                                        "nFiles", JsonValue.Number(decimal(nFiles)) |]
    do if not hide then 
        client.CustomNotification("fsharp/startProgress", message)
    /// Increment the progress bar and change the displayed message to the current file name
    member this.Increment(sourceFile: FileInfo) = 
        if not hide then
            client.CustomNotification("fsharp/incrementProgress", JsonValue.String(sourceFile.Name))
    /// Close the progress bar
    interface IDisposable with 
        member this.Dispose() = 
            if not hide then
                client.CustomNotification("fsharp/endProgress", JsonValue.Null)