module LSP.LanguageServer

open Types
open Parser
open System 
open System.IO
open System.Runtime.Serialization
open System.Runtime.Serialization.Json

let serializerFactory<'a> () = 
    let serializer = new DataContractJsonSerializer(typeof<'a>)
    let doStringify (record: 'a): string = 
        use stream = MemoryStream()
        serializer.WriteObject(stream, record)
        stream.ToArray() |>  System.Text.Encoding.UTF8.GetString
    doStringify
