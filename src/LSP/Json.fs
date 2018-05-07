module LSP.Json

open System
open System.Reflection
open Microsoft.FSharp.Reflection
open Microsoft.FSharp.Reflection.FSharpReflectionExtensions
open System.Text.RegularExpressions
open FSharp.Data

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
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<_ option>

let private isEnum (t: Type) = 
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<seq<_>>
let private implementsEnum (t: Type) = 
    let is = t.GetInterfaces()
    Seq.exists isEnum is

type JsonWriteOptions = {
    customWriters: obj list
}

let defaultJsonWriteOptions: JsonWriteOptions = {
    customWriters = []
}

let private matchWriter (t: Type) (w: obj): bool = 
    let domain, _ = w.GetType() |> FSharpType.GetFunctionElements 
    domain.IsAssignableFrom(t)

let private findWriter (t: Type) (customWriters: obj list): obj option = 
    Seq.tryFind (matchWriter t) customWriters 

let asFun (w: obj): obj -> obj = 
    let invoke = w.GetType().GetMethod("Invoke")
    fun x -> invoke.Invoke(w, [|x|])

let rec private serializer (options: JsonWriteOptions) (t: Type): obj -> string = 
    let custom = findWriter t options.customWriters 
    if custom.IsSome then 
        let fObj = custom.Value
        let _, range = fObj.GetType() |> FSharpType.GetFunctionElements 
        let g = serializer options range
        let f = asFun fObj
        f >> g
    else if t = typeof<bool> then 
        fun o -> sprintf "%b" (unbox<bool> o)
    elif t = typeof<int> then 
        fun o -> sprintf "%d" (unbox<int> o)
    elif t = typeof<char> then 
        fun o -> sprintf "%c" (unbox<char> o) |> escapeStr
    elif t = typeof<string> then 
        fun o -> escapeStr (o :?> string)
    elif t = typeof<Uri> then 
        fun o -> 
            let uri = o :?> Uri 
            uri.ToString() |> escapeStr
    elif t = typeof<JsonValue> then 
        fun o -> 
            let asJson = o :?> JsonValue
            asJson.ToString(JsonSaveOptions.DisableFormatting)
    elif FSharpType.IsRecord t then 
        let fields = FSharpType.GetRecordFields t 
        let serializers = Array.map (fieldSerializer options) fields 
        fun outer ->
            let fieldStrings = Array.map (fun f -> f outer) serializers
            let innerString = String.concat "," fieldStrings
            sprintf "{%s}" innerString
    elif implementsEnum t then 
        let [|innerType|] = t.GetGenericArguments() 
        let serializeInner = serializer options innerType
        fun outer -> 
            let asSeq = outer :?> System.Collections.IEnumerable |> Seq.cast<obj>
            let inners = Seq.map serializeInner asSeq 
            let join = String.Join(',', inners) 
            sprintf "[%s]" join
    elif isOption t then 
        let [|innerType|] = t.GetGenericArguments() 
        let isSomeProp = t.GetProperty "IsSome"
        let isSome outer = isSomeProp.GetValue(None, [|outer|]) :?> bool
        let valueProp = t.GetProperty "Value"
        let serializeInner = serializer options innerType
        fun outer ->
            if isSome outer then 
                valueProp.GetValue outer |> serializeInner
            else "null"
    else 
        raise (Exception (sprintf "Don't know how to serialize %s to JSON" (t.ToString())))
and fieldSerializer (options: JsonWriteOptions) (field: PropertyInfo): obj -> string = 
    let name = escapeStr field.Name
    let innerSerializer = serializer options field.PropertyType
    fun outer -> 
        let inner = field.GetValue outer |> innerSerializer
        sprintf "%s:%s" name inner

let serializerFactory<'T> (options: JsonWriteOptions): 'T -> string = serializer options typeof<'T>

// TODO deserializer that works the same way