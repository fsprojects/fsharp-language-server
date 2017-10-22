namespace LSP

open System
open System.Net.Sockets
open System.Text
open System.Collections.Generic

type Request = TodoRequest
type Response = TodoResponse
type ILanguageServer = 
    abstract member Start: requests: seq<Request> -> seq<Response>

module Tokenizer = 
    type Header = ContentLength of int | EmptyHeader | OtherHeader

    let parseHeader (header: string): Header = 
        let contentLength = "Content-Length: "
        if header.StartsWith contentLength then
            let tail = header.Substring (contentLength.Length)
            let length = Int32.Parse tail 
            ContentLength(length)
        elif header = "" then EmptyHeader
        else OtherHeader

    let takeChar (client: IEnumerator<char>) (expected: char): unit = 
        if not (client.MoveNext()) then 
            raise (Exception (sprintf "Expected %c but input ended" expected))
        elif expected <> client.Current then 
            raise (Exception (sprintf "Expected %c but found %c" expected client.Current))
        else ()

    let takeHeader (client: IEnumerator<char>): string = 
        let acc = StringBuilder()
        while client.MoveNext() && client.Current <> '\r' do 
            acc.Append(client.Current) |> ignore
        takeChar client '\n'
        acc.ToString()

    let takeMessage (client: IEnumerator<char>) (contentLength: int): string =
        raise (NotImplementedException())

    let tokenize (client: seq<char>): seq<string> = 
        raise (NotImplementedException())

module Parser = 
    let parse (body: string): Request = 
        raise (NotImplementedException())

    let stringify (response: Response): string = 
        raise (NotImplementedException())
