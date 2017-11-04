module LSP.Tokenizer

open System
open System.IO
open System.Text

type Header = ContentLength of int | EmptyHeader | OtherHeader

let parseHeader (header: string): Header = 
    let contentLength = "Content-Length: "
    if header.StartsWith contentLength then
        let tail = header.Substring (contentLength.Length)
        let length = Int32.Parse tail 
        ContentLength(length)
    elif header = "" then EmptyHeader
    else OtherHeader

let readLength (byteLength: int) (client: BinaryReader): string = 
    let bytes = client.ReadBytes(byteLength)
    Encoding.UTF8.GetString bytes
    
let readLine (client: BinaryReader): option<string> = 
    let buffer = StringBuilder()
    try
        let mutable endOfLine = false
        while not endOfLine do 
            let nextChar = client.ReadChar()
            if nextChar = '\r' then do 
                assert (client.ReadChar() = '\n')
                endOfLine <- true
            else do 
                buffer.Append nextChar |> ignore
        buffer.ToString() |> Some 
    with 
    | :? EndOfStreamException -> 
        if buffer.Length > 0
            then buffer.ToString() |> Some 
        else
            None

let tokenize (client: BinaryReader): seq<string> = 
    seq {
        let mutable contentLength = -1
        let mutable endOfInput = false
        while not endOfInput do 
            let next = readLine client |> Option.map parseHeader
            match next with 
                | None -> endOfInput <- true 
                | Some (ContentLength l) -> contentLength <- l 
                | Some (EmptyHeader) -> yield readLength contentLength client
                | _ -> ()
    }
