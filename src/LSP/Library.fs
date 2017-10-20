namespace LSP

open System
open System.Net.Sockets

type Request = TodoRequest
type Response = TodoResponse
type ILanguageServer = 
    abstract member Start: requests: seq<Request> -> seq<Response>

module Tokenizer = 
    type Token = ContentLength of int | OtherHeader | Message of string

    let parseHeader (header: string): Token = 
        let contentLength = "Content-Length: "
        if header.StartsWith contentLength then
            let tail = header.Substring (contentLength.Length)
            let length = Int32.Parse tail 
            ContentLength(length)
        else OtherHeader

    let tokenize (client: seq<char>): seq<Token> = 
        raise (NotImplementedException())

module Parser = 
    let parse (body: string): Request = 
        raise (NotImplementedException())

    let stringify (response: Response): string = 
        raise (NotImplementedException())
