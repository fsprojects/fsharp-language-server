module LSP.Json

open System
open System.Reflection
open Microsoft.FSharp.Reflection
open System.Text.RegularExpressions

let private escapeChars = Regex("[\n\r\"]", RegexOptions.Compiled)
let private replaceChars = 
    MatchEvaluator(fun m -> 
        match m.Value with 
        | "\n" -> "\\n" 
        | "\r" -> "\\r" 
        | "\"" -> "\\\"" 
        | v -> v)
let private escapeStr (text:string) =
    let escaped = escapeChars.Replace(text, replaceChars)
    sprintf "\"%s\"" escaped

let private isOption (t: Type) = 
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

let rec private serializer (t: Type): obj -> string = 
    if t = typeof<bool> then 
        fun o -> sprintf "%b" (unbox<bool> o)
    elif t = typeof<int> then 
        fun o -> sprintf "%d" (unbox<int> o)
    elif t = typeof<string> then 
        fun o -> escapeStr (o :?> string)
    elif FSharpType.IsRecord t then 
        let fields = FSharpType.GetRecordFields t 
        let serializers = Array.map fieldSerializer fields 
        fun outer ->
            let fieldStrings = Array.map (fun f -> f outer) serializers
            let innerString = String.concat "," fieldStrings
            sprintf "{%s}" innerString
    elif isOption t then 
        let [|innerType|] = t.GetGenericArguments() 
        let isSomeProp = t.GetProperty "IsSome"
        let isSome outer = isSomeProp.GetValue(None, [|outer|]) :?> bool
        let valueProp = t.GetProperty "Value"
        let serializeInner = serializer innerType
        fun outer ->
            if isSome outer then 
                valueProp.GetValue outer |> serializeInner
            else "null"
    else 
        raise (Exception (sprintf "Don't know how to serialize %s to JSON" (t.ToString())))
and fieldSerializer (field: PropertyInfo): obj -> string = 
    let name = escapeStr field.Name
    let innerSerializer = serializer field.PropertyType
    fun outer -> 
        let inner = field.GetValue outer |> innerSerializer
        sprintf "%s:%s" name inner

let serializerFactory<'T> () = serializer typeof<'T>