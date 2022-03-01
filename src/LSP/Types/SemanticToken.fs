module LSP.SemanticToken
open System
open System.IO
open FSharp.Reflection
open System.Collections.Generic
open FSharp.Compiler.EditorServices
open BaseTypes
//==========================================================
//                  Util Functions
//==========================================================

let inline toCharArray (str:string) = str.ToCharArray()

let lowerCaseFirstChar (str: string) =
    if String.IsNullOrEmpty str
        || Char.IsLower(str, 0) then str else
    let strArr = toCharArray str
    match Array.tryHead strArr with
    | None -> str
    | Some c  ->
        strArr.[0] <- Char.ToLower c
        String (strArr)
//==========================================================
//                  Server capability options
//==========================================================

type SemanticTokensLegend = {
    /// The token types a server uses.
    tokenTypes: string list //TODO:impliment serialization of arrays and change back to arrays
    /// The token modifiers a server uses.
    tokenModifiers: string list //TODO:impliment serialization of arrays and change back to arrays
}

type SemanticTokenFullOptions =
    {
    /// The server supports deltas for full documents.
    delta: bool option
    }
type SemanticTokensOptions = {
    /// The legend used by the server
    legend: SemanticTokensLegend

    /// Server supports providing semantic tokens for a specific range of a document.
    range:  bool option

    /// Server supports providing semantic tokens for a full document.
    full:  bool option // technically this is a union with SemanticTokenFullOptions, but we can jut return the bool version
}

//==========================================================
//                  LSP Params
//==========================================================


type SemanticTokensParams = {
    textDocument: TextDocumentIdentifier
}

type SemanticTokensDeltaParams = {
    textDocument: TextDocumentIdentifier
    /// The result id of a previous response. The result Id can either point to
    /// a full response or a delta response depending on what was received last.
    previousResultId: string
}

type SemanticTokensRangeParams = {
    textDocument: TextDocumentIdentifier
    range: Range
}

type SemanticTokens = {
    /// An optional result id. If provided and clients support delta updating
    /// the client will include the result id in the next semantic token request.
    /// A server can then instead of computing all semantic tokens again simply
    /// send a delta.
    resultId: string option
    data: uint32 list //TODO: cahnge to array when can serializer fixed
}

type SemanticTokensEdit = {
    /// The start offset of the edit.
    start: uint32

    /// The count of elements to remove.
    seleteCount: uint32

    /// The elements to insert.
    sata: uint32 list option //TODO: cahnge to array when can serializer fixed
}

type SemanticTokensDelta = {
    resultId: string option

    /// The semantic token edits to transform a previous result into a new
    /// result.
    edits: SemanticTokensEdit list; //TODO: cahnge to array when can serializer fixed
}








[<RequireQualifiedAccess>]
type SemanticTokenTypes =
(* implementation note: these indexes map to array indexes *)
(* LSP-provided types *)

| Namespace = 0
/// Represents a generic type. Acts as a fallback for types which
/// can't be mapped to a specific type like class or enum.
| Type = 1
| Class = 2
| Enum = 3
| Interface = 4
| Struct = 5
| TypeParameter = 6
| Parameter = 7
| Variable = 8
| Property = 9
| EnumMember = 10
| Event = 11
| Function = 12
| Method = 13
| Macro = 14
| Keyword = 15
| Modifier = 16
| Comment = 17
| String = 18
| Number = 19
| Regexp = 20
| Operator = 21
(* our custom token types *)
| Member = 22
/// computation expressions
| Cexpr = 23
| Text = 24

[<RequireQualifiedAccess; Flags>]
type SemanticTokenModifier =
(* implementation note: these are defined as bitflags to make it easy to calculate them *)
(* LSP-defined modifiers *)
| Declaration    =              0b1
| Definition     =             0b10
| Readonly       =            0b100
| Static         =           0b1000
| Deprecated     =         0b1_0000
| Abstract       =        0b10_0000
| Async          =       0b100_0000
| Modification   =      0b1000_0000
| Documentation  =    0b1_0000_0000
| DefaultLibrary =   0b10_0000_0000
(* custom modifiers *)
| Mutable        =  0b100_0000_0000
| Disposable     = 0b1000_0000_0000


let map (t: SemanticClassificationType) : SemanticTokenTypes * SemanticTokenModifier list =
    match t with
    | SemanticClassificationType.Operator -> SemanticTokenTypes.Operator, []
    | SemanticClassificationType.ReferenceType
    | SemanticClassificationType.Type
    | SemanticClassificationType.TypeDef
    | SemanticClassificationType.ConstructorForReferenceType -> SemanticTokenTypes.Type, []
    | SemanticClassificationType.ValueType
    | SemanticClassificationType.ConstructorForValueType -> SemanticTokenTypes.Struct, []
    | SemanticClassificationType.UnionCase
    | SemanticClassificationType.UnionCaseField -> SemanticTokenTypes.EnumMember, []
    | SemanticClassificationType.Function
    | SemanticClassificationType.Method
    | SemanticClassificationType.ExtensionMethod -> SemanticTokenTypes.Function, []
    | SemanticClassificationType.Property -> SemanticTokenTypes.Property, []
    | SemanticClassificationType.MutableVar
    | SemanticClassificationType.MutableRecordField -> SemanticTokenTypes.Member, [SemanticTokenModifier.Mutable]
    | SemanticClassificationType.Module
    | SemanticClassificationType.Namespace -> SemanticTokenTypes.Namespace, []
    | SemanticClassificationType.Printf -> SemanticTokenTypes.Regexp, []
    | SemanticClassificationType.ComputationExpression -> SemanticTokenTypes.Cexpr, []
    | SemanticClassificationType.IntrinsicFunction -> SemanticTokenTypes.Function, []
    | SemanticClassificationType.Enumeration -> SemanticTokenTypes.Enum, []
    | SemanticClassificationType.Interface -> SemanticTokenTypes.Interface, []
    | SemanticClassificationType.TypeArgument -> SemanticTokenTypes.TypeParameter, []
    | SemanticClassificationType.DisposableTopLevelValue
    | SemanticClassificationType.DisposableLocalValue -> SemanticTokenTypes.Variable, [ SemanticTokenModifier.Disposable ]
    | SemanticClassificationType.DisposableType -> SemanticTokenTypes.Type, [ SemanticTokenModifier.Disposable ]
    | SemanticClassificationType.Literal -> SemanticTokenTypes.Variable, [SemanticTokenModifier.Readonly; SemanticTokenModifier.DefaultLibrary]
    | SemanticClassificationType.RecordField
    | SemanticClassificationType.RecordFieldAsFunction -> SemanticTokenTypes.Property, [SemanticTokenModifier.Readonly]
    | SemanticClassificationType.Exception
    | SemanticClassificationType.Field
    | SemanticClassificationType.Event
    | SemanticClassificationType.Delegate
    | SemanticClassificationType.NamedArgument -> SemanticTokenTypes.Member, []
    | SemanticClassificationType.Value
    | SemanticClassificationType.LocalValue -> SemanticTokenTypes.Variable, []
    | SemanticClassificationType.Plaintext -> SemanticTokenTypes.Text, []

/// generate a TokenLegend from an enum representing the token types and the
/// token modifiers.
///
/// since the token types and modifiers are int-backed names, we follow the
/// following logic to create the backing string arrays for the legends:
///   * iterate the enum values
///   * get the enum name
///   * lowercase the first char because of .net naming conventions
let createTokenLegend<'types, 'modifiers when 'types : enum<int> and
                                            'types: (new : unit -> 'types) and
                                            'types: struct and
                                            'types :> Enum and
                                            'modifiers: enum<int> and
                                            'modifiers: (new : unit -> 'modifiers) and
                                            'modifiers: struct and
                                            'modifiers :> Enum> : SemanticTokensLegend =
    let tokenTypes = Enum.GetNames<'types>() |> Array.map lowerCaseFirstChar
    let tokenModifiers = Enum.GetNames<'modifiers>() |> Array.map lowerCaseFirstChar
    {
        tokenModifiers = tokenModifiers|>Array.toList
        tokenTypes = tokenTypes|>Array.toList
    }


/// <summary>
/// Encodes an array of ranges + token types/mods into the LSP SemanticTokens' data format.
/// Each item in our range array is turned into 5 integers:
///   * line number delta relative to the previous entry
///   * start column delta relative to the previous entry
///   * length of the token
///   * token type int
///   * token modifiers encoded as bit flags
/// </summary>
/// <param name="rangesAndHighlights"></param>
/// <returns></returns>
let encodeSemanticHighlightRanges (rangesAndHighlights: (struct(Range * SemanticTokenTypes * SemanticTokenModifier list)) array) =
  let fileStart = { start = { line = 0; character = 0}; ``end`` = { line = 0; character = 0 } }
  let computeLine (prev: LSP.BaseTypes.Range) ((range, ty, mods): struct(LSP.BaseTypes.Range * SemanticTokenTypes * SemanticTokenModifier list)): uint32 [] =
    let lineDelta =
      if prev.start.line = range.start.line then 0u
      else uint32 (range.start.line - prev.start.line)
    let charDelta =
      if lineDelta = 0u
      then uint32 (range.start.character - prev.start.character)
      else uint32 range.start.character
    let tokenLen = uint32 (range.``end``.character - range.start.character)
    let tokenTy = uint32 ty
    let tokenMods =
      match mods with
      | [] -> 0u
      | [ single ] -> uint32 single
      | mods ->
        // because the mods are all bit flags, we can just OR them together
        let flags = mods |> List.reduce (( ||| ))
        uint32 flags
    [| lineDelta; charDelta; tokenLen; tokenTy; tokenMods |]

  match rangesAndHighlights.Length with
  | 0 -> None
  /// only 1 entry, so compute the line from the 0 position
  | 1 ->
    Some (
      computeLine fileStart rangesAndHighlights.[0]
    )
  | _ ->
    let finalArray = Array.zeroCreate (rangesAndHighlights.Length * 5) // 5 elements per entry
    let mutable prev = fileStart
    let mutable idx = 0
    // trying to fill the `finalArray` in a single pass here, since its size is known
    for (currentRange, _, _) as item in rangesAndHighlights do
      let result = computeLine prev item
      // copy the 5-array of results into the final array
      finalArray.[idx] <- result.[0]
      finalArray.[idx+1] <- result.[1]
      finalArray.[idx+2] <- result.[2]
      finalArray.[idx+3] <- result.[3]
      finalArray.[idx+4] <- result.[4]
      prev <- currentRange
      idx <- idx + 5
    Some finalArray
