module FSharpLanguageServer.ToolTips.ToolTip
open System
open System.IO
open System.Xml
open System.Collections.Generic
open System.Text.RegularExpressions
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Formatting
open XmlDoc
open LSP.Log

[<RequireQualifiedAccess>]
type FormatCommentStyle =
  | Legacy
  | FullEnhanced
  | SummaryOnly
  | Documentation

// --------------------------------------------------------------------------------------
// Formatting of tool-tip information displayed in F# IntelliSense
// --------------------------------------------------------------------------------------
let private buildFormatComment cmt (formatStyle: FormatCommentStyle) (typeDoc: string option) =
  match cmt with
  | FSharpXmlDoc.FromXmlText xmldoc ->
    try
      let document = xmldoc.GetXmlText()
      // We create a "fake" XML document in order to use the same parser for both libraries and user code
      let xml = sprintf "<fake>%s</fake>" document
      let doc = XmlDocument()
      doc.LoadXml(xml)

      // This try to mimic how we found the indentation size when working a real XML file
      let rec findIndentationSize (lines: string list) =
        match lines with
        | head :: tail ->
          let lesserThanIndex = head.IndexOf("<")

          if lesserThanIndex <> -1 then
            lesserThanIndex
          else
            findIndentationSize tail
        | [] -> 0

      let indentationSize =
        xmldoc.GetElaboratedXmlLines()
        |> Array.toList
        |> findIndentationSize

      let xmlDoc = XmlDocMember(doc, indentationSize, 0)

      match formatStyle with
      | FormatCommentStyle.Legacy -> xmlDoc.ToString()
      | FormatCommentStyle.SummaryOnly -> xmlDoc.ToSummaryOnlyString()
      | FormatCommentStyle.FullEnhanced -> xmlDoc.ToFullEnhancedString()
      | FormatCommentStyle.Documentation -> xmlDoc.ToDocumentationString()

    with
    | ex ->

      (dprintfn "TipFormatter - Error while parsing the doc comment %A" ex)
      sprintf
        "An error occured when parsing the doc comment, please check that your doc comment is valid.\n\nMore info can be found LSP output"

  | FSharpXmlDoc.FromXmlFile (dllFile, memberName) ->
    match getXmlDoc dllFile with
    | Some doc when doc.ContainsKey memberName ->
      let typeDoc =
        match typeDoc with
        | Some s when doc.ContainsKey s ->
          match formatStyle with
          | FormatCommentStyle.Legacy -> doc.[s].ToString()
          | FormatCommentStyle.SummaryOnly -> doc.[s].ToSummaryOnlyString()
          | FormatCommentStyle.FullEnhanced -> doc.[s].ToFullEnhancedString()
          | FormatCommentStyle.Documentation -> doc.[s].ToDocumentationString()
        | _ -> ""

      match formatStyle with
      | FormatCommentStyle.Legacy ->
        doc.[memberName].ToString()
        + (if typeDoc <> "" then
             "\n\n" + typeDoc
           else
             "")
      | FormatCommentStyle.SummaryOnly ->
        doc.[memberName].ToSummaryOnlyString()
        + (if typeDoc <> "" then
             "\n\n" + typeDoc
           else
             "")
      | FormatCommentStyle.FullEnhanced ->
        doc.[memberName].ToFullEnhancedString()
        + (if typeDoc <> "" then
             "\n\n" + typeDoc
           else
             "")
      | FormatCommentStyle.Documentation ->
        doc.[memberName].ToDocumentationString()
        + (if typeDoc <> "" then
             "\n\n" + typeDoc
           else
             "")
    | _ -> ""
  | _ -> ""

let formatTaggedText (t: TaggedText) : string =
  match t.Tag with
  | TextTag.ActivePatternResult
  | TextTag.UnionCase
  | TextTag.Delegate
  | TextTag.Field
  | TextTag.Keyword
  | TextTag.LineBreak
  | TextTag.Local
  | TextTag.RecordField
  | TextTag.Method
  | TextTag.Member
  | TextTag.ModuleBinding
  | TextTag.Function
  | TextTag.Module
  | TextTag.Namespace
  | TextTag.NumericLiteral
  | TextTag.Operator
  | TextTag.Parameter
  | TextTag.Property
  | TextTag.Space
  | TextTag.StringLiteral
  | TextTag.Text
  | TextTag.Punctuation
  | TextTag.UnknownType
  | TextTag.UnknownEntity -> t.Text
  | TextTag.Enum
  | TextTag.Event
  | TextTag.ActivePatternCase
  | TextTag.Struct
  | TextTag.Alias
  | TextTag.Class
  | TextTag.Union
  | TextTag.Interface
  | TextTag.Record
  | TextTag.TypeParameter -> $"`{t.Text}`"

let formatTaggedTexts = Array.map formatTaggedText >> String.concat ""

let formatGenericParameters (typeMappings: TaggedText [] list) =
  typeMappings
  |> List.map (fun typeMap -> $"* {formatTaggedTexts typeMap}")
  |> String.concat nl

let formatTip (ToolTipText tips) : (string * string) list list =
  tips
  |> List.choose (function
    | ToolTipElement.Group items ->
      let getRemarks (it: ToolTipElementData) =
        it.Remarks
        |> Option.map formatTaggedTexts
        |> Option.defaultValue ""

      let makeTooltip (tipElement: ToolTipElementData) =
        let header =
          formatTaggedTexts tipElement.MainDescription
          + getRemarks tipElement

        let body = buildFormatComment tipElement.XmlDoc FormatCommentStyle.Legacy None
        header, body

      items |> List.map makeTooltip |> Some

    | ToolTipElement.CompositionError (error) -> Some [ ("<Note>", error) ]
    | _ -> None)

let formatTipEnhanced
  (ToolTipText tips)
  (signature: string)
  (footer: string)
  (typeDoc: string option)
  (formatCommentStyle: FormatCommentStyle)
  : (string * string * string) list list =
  tips
  |> List.choose (function
    | ToolTipElement.Group items ->
      Some(
        items
        |> List.map (fun i ->
          let comment =
            if i.TypeMapping.IsEmpty then
              buildFormatComment i.XmlDoc formatCommentStyle typeDoc
            else
              buildFormatComment i.XmlDoc formatCommentStyle typeDoc
              + nl
              + nl
              + "**Generic Parameters**"
              + nl
              + nl
              + formatGenericParameters i.TypeMapping

          (signature, comment, footer))
      )
    | ToolTipElement.CompositionError (error) -> Some [ ("<Note>", error, "") ]
    | _ -> None)

let formatDocumentation
  (ToolTipText tips)
  ((signature, (constructors, fields, functions, interfaces, attrs, ts)): string * (string [] * string [] * string [] * string [] * string [] * string []))
  (footer: string)
  (cn: string)
  =
  tips
  |> List.choose (function
    | ToolTipElement.Group items ->
      Some(
        items
        |> List.map (fun i ->
          let comment =
            if i.TypeMapping.IsEmpty then
              buildFormatComment i.XmlDoc FormatCommentStyle.Documentation None
            else
              buildFormatComment i.XmlDoc FormatCommentStyle.Documentation None
              + nl
              + nl
              + "**Generic Parameters**"
              + nl
              + nl
              + formatGenericParameters i.TypeMapping

          (signature, constructors, fields, functions, interfaces, attrs, ts, comment, footer, cn))
      )
    | ToolTipElement.CompositionError (error) -> Some [ ("<Note>", [||], [||], [||], [||], [||], [||], error, "", "") ]
    | _ -> None)

let formatDocumentationFromXmlSig
  (xmlSig: string)
  (assembly: string)
  ((signature, (constructors, fields, functions, interfaces, attrs, ts)): string * (string [] * string [] * string [] * string [] * string [] * string []))
  (footer: string)
  (cn: string)
  =
  let xmlDoc = FSharpXmlDoc.FromXmlFile(assembly, xmlSig)
  let comment = buildFormatComment xmlDoc FormatCommentStyle.Documentation None
  [ [ (signature, constructors, fields, functions, interfaces, attrs, ts, comment, footer, cn) ] ]

/// use this when you want the raw text strings, for example in fsharp/signature calls
let unformattedTexts (t: TaggedText []) = t |> Array.map (fun t -> t.Text) |> String.concat ""

let extractSignature (ToolTipText tips) =
  let getSignature (t: TaggedText []) =
    let str = unformattedTexts t
    let nlpos = str.IndexOfAny([| '\r'; '\n' |])

    let firstLine =
      if nlpos > 0 then
        str.[0..nlpos - 1]
      else
        str

    if firstLine.StartsWith("type ", StringComparison.Ordinal) then
      let index = firstLine.LastIndexOf("=", StringComparison.Ordinal)

      if index > 0 then
        firstLine.[0..index - 1]
      else
        firstLine
    else
      firstLine

  let firstResult x =
    match x with
    | ToolTipElement.Group gs ->
      List.tryPick
        (fun (t: ToolTipElementData) ->
          if not (Array.isEmpty t.MainDescription) then
            Some t.MainDescription
          else
            None)
        gs
    | _ -> None

  tips
  |> Seq.tryPick firstResult
  |> Option.map getSignature
  |> Option.defaultValue ""

/// extracts any generic parameters present in this tooltip, rendering them as plain text
let extractGenericParameters (ToolTipText tips) =
  let firstResult x =
    match x with
    | ToolTipElement.Group gs ->
      List.tryPick
        (fun (t: ToolTipElementData) ->
          if not (t.TypeMapping.IsEmpty) then
            Some t.TypeMapping
          else
            None)
        gs
    | _ -> None

  tips
  |> Seq.tryPick firstResult
  |> Option.defaultValue []
  |> List.map unformattedTexts