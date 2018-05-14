module LSP.Json

open System
open System.Reflection
open Microsoft.FSharp.Reflection
open Microsoft.FSharp.Reflection.FSharpReflectionExtensions
open System.Text.RegularExpressions
open LSP.Json

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

let private isSeq (t: Type) = 
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<seq<_>>
let private implementsSeq (t: Type) = 
    let is = t.GetInterfaces()
    Seq.exists isSeq is

let private isList (t: Type) = 
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

let private isMap (t: Type) = 
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Map<_, _>>

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

type MakeHelpers = 
    static member MakeList<'T> (items: obj seq): 'T list = 
        [ for i in items do yield i :?> 'T ]
    static member MakeMap<'T> (items: (string * obj) seq): Map<string, 'T> = 
        items |> Seq.map (fun (k, v) -> k, v :?> 'T) |> Map.ofSeq
    static member MakeOption<'T> (item: obj option): 'T option = 
        match item with 
        | None -> None 
        | Some i -> Some (i :?> 'T)

let private makeList (t: Type) (items: obj seq) = 
    typeof<MakeHelpers>.GetMethod("MakeList").MakeGenericMethod([|t|]).Invoke(null, [|items|])

let private makeMap (t: Type) (kvs: (string * obj) seq) = 
    typeof<MakeHelpers>.GetMethod("MakeMap").MakeGenericMethod([|t|]).Invoke(null, [|kvs|])

let private makeOption (t: Type) (item: obj option) = 
    typeof<MakeHelpers>.GetMethod("MakeOption").MakeGenericMethod([|t|]).Invoke(null, [|item|])

let rec private serializer (options: JsonWriteOptions) (t: Type): obj -> string = 
    let custom = findWriter t options.customWriters 
    if custom.IsSome then 
        let fObj = custom.Value
        let _, range = fObj.GetType() |> FSharpType.GetFunctionElements 
        let g = serializer options range
        let f = asFun fObj
        f >> g
    elif t = typeof<bool> then 
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
    elif implementsSeq t then 
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

type JsonReadOptions = {
    customReaders: obj list
}

let defaultJsonReadOptions: JsonReadOptions = {
    customReaders = []
}

let private matchReader (t: Type) (w: obj): bool = 
    let _, range = w.GetType() |> FSharpType.GetFunctionElements 
    t.IsAssignableFrom(range)

let private findReader (t: Type) (customReaders: obj list): obj option = 
    Seq.tryFind (matchReader t) customReaders 

let rec private deserializer<'T> (options: JsonReadOptions) (t: Type): JsonValue -> obj = 
    let custom = findReader t options.customReaders 
    if custom.IsSome then 
        let domain, _ = custom.Value.GetType() |> FSharpType.GetFunctionElements 
        let deserializeDomain = deserializer options domain
        let deserializeInner = asFun custom.Value 
        deserializeDomain >> deserializeInner
    elif t = typeof<bool> then 
        fun j -> j.AsBoolean() |> box
    elif t = typeof<int> then 
        fun j -> j.AsInteger() |> box
    elif t = typeof<char> then 
        fun j ->
            let s = j.AsString()
            if s.Length = 1 then s.[0] |> box
            else raise(Exception(sprintf "Expected char but found '%s'" s))
    elif t = typeof<string> then 
        fun j -> j.AsString() |> box
    elif t = typeof<Uri> then 
        fun j -> Uri(j.AsString()) |> box
    elif t = typeof<JsonValue> then 
        fun j -> j |> box
    elif isList t then 
        let [|innerType|] = t.GetGenericArguments() 
        let deserializeInner = deserializer options innerType
        fun j -> j.AsArray() |> Seq.map deserializeInner |> makeList innerType |> box
    elif isMap t then 
        let [|stringType; valueType|] = t.GetGenericArguments()
        if stringType <> typeof<string> then raise (Exception (sprintf "Keys of %A are not strings" t))
        let deserializeInner = deserializer options valueType 
        fun j -> 
            j.Properties() |> Seq.map (fun (k, v) -> k, deserializeInner v) |> makeMap valueType 
    elif isOption t then 
        let [|innerType|] = t.GetGenericArguments()
        let deserializeInner = deserializer options innerType
        fun j ->
            if j = JsonValue.Null then 
                None |> makeOption innerType |> box 
            else 
                deserializeInner j |> Some |> makeOption innerType |> box
    elif FSharpType.IsRecord t then 
        let fields = FSharpType.GetRecordFields t 
        let readers = Array.map (fieldDeserializer options) fields 
        fun j -> 
            let array = [| for field, reader in readers do 
                                yield reader j |]
            FSharpValue.MakeRecord(t, array)
    else 
        raise (Exception (sprintf "Don't know how to deserialize %A from JSON" t))
and fieldDeserializer (options: JsonReadOptions) (field: PropertyInfo): string * (JsonValue -> obj) = 
    let deserializeInner = deserializer options field.PropertyType
    let deserializeField (j: JsonValue) = 
        let value = j.TryGetProperty(field.Name) |> Option.defaultValue JsonValue.Null
        deserializeInner value |> box
    field.Name, deserializeField

let deserializerFactory<'T> (options: JsonReadOptions): JsonValue -> 'T = 
    let d = deserializer options typeof<'T>
    fun s -> d s :?> 'T