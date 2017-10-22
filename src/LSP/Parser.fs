namespace LSP

open System
open FSharp.Data

module Parser = 
    type RequestMessage = RequestMessage of id: int * method: string * params: option<JsonValue>

    let parseRequestMessage (json: string): RequestMessage = 
        raise (NotImplementedException())