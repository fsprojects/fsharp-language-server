module LSP.Uris

open System
open System.IO

let asPath(uri: Uri): string = 
    let str = Uri.UnescapeDataString(uri.OriginalString)
    if not uri.IsFile then 
        raise(Exception(str + " is not a file"))
    else if Path.DirectorySeparatorChar = '\\' then 
        str.Substring("file:///".Length).Replace('/', '\\')
    else
        str.Substring("file://".Length)

let asFile(uri: Uri): FileInfo = 
    FileInfo(asPath(uri))