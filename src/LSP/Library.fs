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

    let takeChar (client: IEnumerator<char>) (expected: char): bool = 
        if not (client.MoveNext()) then 
            false
        elif expected <> client.Current then 
            raise (Exception (sprintf "Expected %c but found %c" expected client.Current))
        else true

    let takeHeader (client: IEnumerator<char>): option<string> = 
        let acc = StringBuilder()
        while client.MoveNext() && client.Current <> '\r' do 
            acc.Append(client.Current) |> ignore
        if takeChar client '\n' then
            Some (acc.ToString())
        else
            None


    let takeMessage (client: IEnumerator<char>) (contentLength: int): string =
        let acc = StringBuilder()
        for remaining = contentLength downto 1 do 
            if not (client.MoveNext()) then 
                raise (Exception(sprintf "Expected %d more characters in message but input ended" remaining))
            acc.Append(client.Current) |> ignore
        takeChar client '\r' |> ignore
        takeChar client '\n' |> ignore
        acc.ToString()

    let tokenize (client: seq<char>): seq<string> = 
        seq {
            let enum = client.GetEnumerator()
            let mutable contentLength = -1
            let mutable endOfInput = false
            while not endOfInput do 
                let next = Option.map parseHeader (takeHeader enum)
                match next with 
                    | None -> endOfInput <- true 
                    | Some (ContentLength l) -> contentLength <- l 
                    | Some (EmptyHeader) -> yield takeMessage enum contentLength 
                    | _ -> ()
        }

module Parser = 
    let parse (body: string): Request = 
        raise (NotImplementedException())

    let stringify (response: Response): string = 
        raise (NotImplementedException())
