module FSharpLanguageServer.SyntaxOps

// Forked from https://github.com/dotnet/fsharp/blob/60a2fa663a3c4aed3f03c8bfc6f5e05b04284f23/src/fsharp/range.fs

open FSharp.Compiler.Syntax
open FSharp.Compiler.Text.Range

let ident (s, r) = Ident(s, r)

let textOfId (id: Ident) = id.idText

let pathOfLid lid = List.map textOfId lid

let arrPathOfLid lid = Array.ofList (pathOfLid lid)

let textOfPath path = String.concat "." path

let textOfLid lid = textOfPath (pathOfLid lid)

let rangeOfLid (lid: Ident list) =
    match lid with
    | [] -> failwith "rangeOfLid"
    | [id] -> id.idRange
    | h :: t -> unionRanges h.idRange (List.last t).idRange
