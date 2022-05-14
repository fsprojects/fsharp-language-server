namespace LSP
open System
open System.IO
type NormedFileInfo=FileInfo
[<AutoOpen>]
module Utils=
    let normalizeDriveLetter (path:string)=
        let a=path.ToCharArray() 
        a[0]<-a[0] |>Char.ToLowerInvariant
        new String(a)
    let normedFileInfo path =
        path|> normalizeDriveLetter|>NormedFileInfo