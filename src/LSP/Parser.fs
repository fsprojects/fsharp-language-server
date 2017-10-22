namespace LSP

open System
open FSharp.Data
open FSharp.Data.JsonExtensions

module Parser = 
    type Message = 
    | RequestMessage of id: int * method: string * body: option<JsonValue>
    | NotificationMessage of method: string * body: option<JsonValue>

    let parse (jsonText: string): Message = 
        let json = JsonValue.Parse jsonText
        let jsonRpcVersion = json.GetProperty("jsonrpc").AsString()
        assert (jsonRpcVersion = "2.0")
        let maybeId = json.TryGetProperty("id") |> Option.map JsonExtensions.AsInteger
        let method = json.GetProperty("method").AsString()
        let body = json.TryGetProperty("params")

        match maybeId with
        | Some id -> RequestMessage (id, method, body)
        | None -> NotificationMessage (method, body)
        
