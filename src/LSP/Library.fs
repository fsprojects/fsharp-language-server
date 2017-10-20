namespace LSP

open System
open System.Net.Sockets

type Request = TodoRequest
type Response = TodoResponse
type ILanguageServer = 
    abstract member Start: requests: seq<Request> -> seq<Response>

module Parser = 
    type Header = ContentLength of int | Other

    let parseHeader (header: string): Header = 
        let contentLength = "Content-Length: "
        if header.StartsWith contentLength then
            let tail = header.Substring (contentLength.Length)
            let length = Int32.Parse tail 
            ContentLength(length)
        else Other

    let messages (client: Socket): seq<string> = 
        raise (NotImplementedException())

    let parse (body: string): Request = 
        raise (NotImplementedException())

    let stringify (response: Response): string = 
        raise (NotImplementedException())
