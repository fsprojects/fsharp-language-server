namespace LSP
open System
open System.IO
type NormedFileInfo=FileInfo
[<AutoOpen>]
module Utils=
    let normalizeDriveLetter (path:string)=
        let a=path.ToCharArray() 
        a[0]<-a[0] |>Char.ToUpperInvariant
        new String(a)
    let FileInfo path =
        path|> normalizeDriveLetter|>NormedFileInfo