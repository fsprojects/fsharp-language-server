namespace LSP
open System
open System.IO
open System.Text.RegularExpressions
type NormedFileInfo=FileInfo
[<AutoOpen>]
module Utils=
    let normalizeDriveLetter (path:string)=
        if  Regex("(^\w:)").Match(path).Success then
            let a=path.ToCharArray() 
            a[0]<-(a[0] |>Char.ToUpperInvariant)
            new String(a)
        else path    
    let normedFileInfo path =
        path|> normalizeDriveLetter|>NormedFileInfo