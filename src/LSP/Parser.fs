namespace LSP

open System
open FSharp.Data
open FSharp.Data.JsonExtensions

module Parser = 
    type RequestMessage = RequestMessage of id: int * method: string * params: option<JsonValue>

    let parseRequestMessage (jsonText: string): RequestMessage = 
        let json = JsonValue.Parse jsonText
        let jsonRpcVersion = json.GetProperty("jsonrpc").AsString()
        assert (jsonRpcVersion = "2.0")
        let id = json.GetProperty("id").AsInteger()
        let method = json.GetProperty("method").AsString()
        let params = json.TryGetProperty("params")
        RequestMessage (id, method, params)
