module FSharpLanguageServer.SemanticTokenization
open System
open LSP.SemanticToken
open LSP
open FSharp.Compiler.EditorServices

open FSharp.Compiler.Text
open FSharp.Compiler.CodeAnalysis
open LSP.Types

// See https://code.visualstudio.com/api/language-extensions/semantic-highlight-guide#semantic-token-scope-map for the built-in scopes
// if new token-type strings are added here, make sure to update the 'legend' in any downstream consumers.
let map (t: SemanticClassificationType) : string =
      match t with
      | SemanticClassificationType.Operator -> "operator"
      | SemanticClassificationType.ReferenceType
      | SemanticClassificationType.Type
      | SemanticClassificationType.TypeDef
      | SemanticClassificationType.ConstructorForReferenceType -> "type"
      | SemanticClassificationType.ValueType
      | SemanticClassificationType.ConstructorForValueType -> "struct"
      | SemanticClassificationType.UnionCase
      | SemanticClassificationType.UnionCaseField -> "enumMember"
      | SemanticClassificationType.Function
      | SemanticClassificationType.Method
      | SemanticClassificationType.ExtensionMethod -> "function"
      | SemanticClassificationType.Property -> "property"
      | SemanticClassificationType.MutableVar
      | SemanticClassificationType.MutableRecordField -> "mutable"
      | SemanticClassificationType.Module
      | SemanticClassificationType.Namespace -> "namespace"
      | SemanticClassificationType.Printf -> "regexp"
      | SemanticClassificationType.ComputationExpression -> "cexpr"
      | SemanticClassificationType.IntrinsicFunction -> "function"
      | SemanticClassificationType.Enumeration -> "enum"
      | SemanticClassificationType.Interface -> "interface"
      | SemanticClassificationType.TypeArgument -> "typeParameter"
      | SemanticClassificationType.DisposableTopLevelValue
      | SemanticClassificationType.DisposableLocalValue
      | SemanticClassificationType.DisposableType -> "disposable"
      | SemanticClassificationType.Literal -> "variable.readonly.defaultLibrary"
      | SemanticClassificationType.RecordField
      | SemanticClassificationType.RecordFieldAsFunction -> "property.readonly"
      | SemanticClassificationType.Exception
      | SemanticClassificationType.Field
      | SemanticClassificationType.Event
      | SemanticClassificationType.Delegate
      | SemanticClassificationType.NamedArgument -> "member"
      | SemanticClassificationType.Value
      | SemanticClassificationType.LocalValue -> "variable"
      | SemanticClassificationType.Plaintext -> "text"


///Converts FCS token information to LSP Token information
let private convertToken (tokens:SemanticClassificationItem[] option)=
    match tokens with
      | None ->
          None
      | Some rangesAndHighlights ->
          let lspTypedRanges =
              rangesAndHighlights
              |> Array.map (fun {Range=fcsRange;Type= fcsTokenType} ->

                  let ty, mods = SemanticToken.map fcsTokenType
                  struct(Conversions.asRange fcsRange, ty, mods)
              )
          match SemanticToken.encodeSemanticHighlightRanges lspTypedRanges with
          | None ->
              None
          | Some encoded ->
              (Some { data = encoded|>Array.toList; resultId = None }) // TODO: provide a resultId when we support delta ranges


let posEq (p1: pos) (p2: pos) = p1 = p2
//All taken from FSAC semnantic token code

/// given an enveloping range and the sub-ranges it overlaps, split out the enveloping range into a
/// set of range segments that are non-overlapping with the children
let private segmentRanges (parentRange: Range) (childRanges: Range []): Range [] =
    let firstSegment = Range.mkRange parentRange.FileName parentRange.Start childRanges.[0].Start // from start of parent to start of first child
    let lastSegment = Range.mkRange parentRange.FileName (Array.last childRanges).End parentRange.End // from end of last child to end of parent
    // now we can go pairwise, emitting a new range for the area between each end and start
    let innerSegments =
        childRanges |> Array.pairwise |> Array.map (fun (left, right) -> Range.mkRange parentRange.FileName left.End right.Start)

    [|
        // note that the first and last segments can be zero-length.
        // in that case we should not emit them because it confuses the
        // encoding algorithm
        if posEq firstSegment.Start firstSegment.End then () else firstSegment
        yield! innerSegments
        if posEq lastSegment.Start lastSegment.End then () else lastSegment
    |]

/// TODO: LSP technically does now know how to handle overlapping, nested and multiline ranges, but
/// as of 3 February 2021 there are no good examples of this that I've found, so we still do this
/// because LSP doesn't know how to handle overlapping/nested ranges, we have to dedupe them here
let private scrubRanges (highlights: SemanticClassificationItem array): SemanticClassificationItem array =
  let startToken = fun( {Range=m}:SemanticClassificationItem) -> m.Start.Line, m.Start.Column
  highlights
  |> Array.sortBy startToken
  |> Array.groupBy (fun {Range=r} -> r.StartLine)
  |> Array.collect (fun (_, highlights) ->

      // split out any ranges that contain other ranges on this line into the non-overlapping portions of that range
      let expandParents ({Range=parentRange;Type=tokenType}:SemanticClassificationItem as p) =
          let children =
              highlights
              |> Array.except [p]
              |> Array.choose (fun {Range=childRange} -> if Range.rangeContainsRange parentRange childRange then Some childRange else None)
          match children with
          | [||] -> [| p |]
          | children ->
              let sortedChildren = children |> Array.sortBy (fun r -> r.Start.Line, r.Start.Column)
              segmentRanges parentRange sortedChildren
              |> Array.map (fun subRange -> SemanticClassificationItem((subRange,tokenType)) )

      highlights
      |> Array.collect expandParents
  )
  |> Array.sortBy startToken

///Gets the tokens for semantic tokenization
///This is done by getting typecheck data and then getting the individual semntic clasifcations of the tokens in that data
///The checking can be done only in a certain range using the Range attribute. I'm not sure if this is really much faster or not 
//TODO: benchmark this process to find out whether only doing a certian range is actually faster
let getSemanticTokens range (typeCheckResults:Result<(FSharpParseFileResults * FSharpCheckFileResults),Diagnostic list>)=
    async{
        let tokens=
            match typeCheckResults with
            |Ok(parse,check)->
                let ranges=check.GetSemanticClassification range
                //If we don't do the crubbing heaps of the tokesn don't show up properly in the edditor
                let filteredRanges = scrubRanges ranges
                Some filteredRanges    
            |_->None
        return convertToken tokens
        }