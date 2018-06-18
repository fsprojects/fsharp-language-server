namespace FSharpLanguageServer

open System.IO
open System.Collections.Concurrent
open System.Threading

type DebounceCheck(check: FileInfo -> Async<unit>, delayMs: int) = 
    let todo = new ConcurrentDictionary<string, FileInfo>()
    let mutable cancel = new CancellationTokenSource()
    let doCheck(file: FileInfo) = 
        async {
            do! check(file)
            todo.TryRemove(file.FullName) |> ignore
        }
    let doCheckAll() = 
        async {
            for file in todo.Values do 
                do! doCheck(file)
        }
    member this.CheckLater(file: FileInfo) = 
        // Add this file to the todo list
        todo.TryAdd(file.FullName, file) |> ignore
        // Reset the check-countdown
        cancel.Cancel()
        cancel <- new CancellationTokenSource()
        // Start a new check-countdown
        Async.Start(async {
            do! Async.Sleep(delayMs)
            do! doCheckAll()
        }, cancel.Token)
    member this.CheckNow() = 
        cancel.Cancel()
        doCheckAll()


