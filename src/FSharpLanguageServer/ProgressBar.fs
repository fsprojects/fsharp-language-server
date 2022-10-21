namespace FSharpLanguageServer

open System
open System.IO
open LSP.Types
open FSharp.Data

/// When we check a long series of files, create a progress bar
type ProgressBar(nFiles: int, title: string, client: ILanguageClient, ?hide: bool) = 
    let token=Guid.NewGuid().ToString()
    let hide = defaultArg hide false
    let mutable processed=0;
    let notifyProgress workDone= client.WorkDoneProgressNotification(token,workDone)
    do if not hide then 
        client.CustomNotification(Random().Next(0,100),"window/workDoneProgress/create",JsonValue.Record([|"token",JsonValue.String(token)|]))
        workDoneProgressBegin(title,Some false,Some <|nFiles.ToString(),Some 0u)|>notifyProgress
    /// Increment the progress bar and change the displayed message to the current file name
    member this.Increment(sourceFile: FileInfo) = 
        if not hide then
            let message = $"processed {sourceFile}. Remaining:{nFiles-processed}"
            let percent=(processed*100)/nFiles|>uint
            workDoneProgressReport(Some false,Some message,Some percent)|>notifyProgress
        processed<- processed+1

    /// Close the progress bar
    interface IDisposable with 
        member this.Dispose() = 
            if not hide then
                workDoneProgressEnd(Some "Done")|>notifyProgress

