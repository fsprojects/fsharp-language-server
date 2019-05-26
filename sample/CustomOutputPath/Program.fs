// issue #58
// Project cracking succeeds, but target dll path is not extracted from the msbuild project.

open System

[<EntryPoint>]
let main argv =
    printfn "Hello World from F#!"
    0 // return an integer exit code
