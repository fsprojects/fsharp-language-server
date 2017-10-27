module LSP.LanguageServer

open System
open TypeShape
open Types
open System.Text.RegularExpressions

let private escapeChars = Regex("[\n\r\"]", RegexOptions.Compiled)
let private replaceChars = 
    MatchEvaluator(fun m -> 
        match m.Value with 
        | "\n" -> "\\n" 
        | "\r" -> "\\r" 
        | "\"" -> "\\\"" 
        | v -> v)
let escapeStr (text:string) =
    let escaped = escapeChars.Replace(text, replaceChars)
    "\"" + escaped + "\""

let rec serializerFactory<'T> (): 'T -> string = 
    let wrap (p: 'a -> string) = unbox<'T -> string> p
    match shapeof<'T> with 
    | Shape.Bool -> wrap(sprintf "%b")
    | Shape.Int32 -> wrap(sprintf "%d")
    | Shape.String -> wrap(escapeStr)
    | Shape.FSharpOption s ->
        s.Accept {
            new IFSharpOptionVisitor<'T -> string> with
                member this.Visit<'a> () =
                    let serializeContents = serializerFactory<'a>()
                    let serializeOption maybe = 
                        match maybe with 
                        | None -> "null"
                        | Some value -> serializeContents value 
                    wrap(serializeOption)
        }
    | other -> raise (Exception (sprintf "Don't know how to serialize %s to JSON" (other.ToString())))
