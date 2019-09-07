module FSharpLanguageServer.SignatureLens

open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open FSharp.Data
open FSharpLanguageServer.Conversions
open LSP.Log
open LSP.Types


let defaultNamespaces =
  [ "Microsoft.FSharp.Core"
    "Microsoft.FSharp.Collections" ]


let scrubTypeName namespaces (fsharpType: FSharpType) =
  let typeText = fsharpType.Format(FSharpDisplayContext.Empty)

  seq {
    for ns: string in namespaces do
      if typeText.StartsWith(ns) then
        yield typeText.Substring(ns.Length + 1)
  }
  |> Seq.tryHead
  |> Option.defaultValue typeText


let rec scrubType namespaces (fsharpType: FSharpType) =
  if fsharpType.IsFunctionType then
    fsharpType.GenericArguments
      |> Seq.map (scrubType namespaces)
      |> String.concat " -> "
  elif fsharpType.IsTupleType then
    fsharpType.GenericArguments
      |> Seq.map (scrubType namespaces)
      |> String.concat " * "
      |> sprintf "(%s)"
  elif fsharpType.IsStructTupleType then
    fsharpType.GenericArguments
      |> Seq.map (scrubType namespaces)
      |> String.concat " * "
      |> sprintf "struct (%s)"
  elif fsharpType.IsAbbreviation then
    scrubType namespaces fsharpType.AbbreviatedType
  else
    try
      if fsharpType.GenericArguments.Count = 0 then
        scrubTypeName namespaces fsharpType
      else
        let arguments =
          fsharpType.GenericArguments
            |> Seq.map (scrubType namespaces)
            |> String.concat ", "

        match fsharpType.TypeDefinition.DisplayName with
        | "[]" -> sprintf "%s []" arguments
        | name -> sprintf "%s<%s>" name arguments
    with exn ->
      dprintfn "Trouble parsing type: %s" exn.Message
      scrubTypeName namespaces fsharpType


let create namespaces (fsharpType: FSharpType) range =
  let command =
    { title = scrubType namespaces fsharpType
      command = ""
      arguments = [] }

  { range = asRange(range)
    command = Some(command)
    data = JsonValue.Null }


let private lineLensFromEntity namespaces (entity: FSharpMemberOrFunctionOrValue) =
  create namespaces
    <| entity.FullType
    <| entity.DeclarationLocation.StartRange


let private lineLensFromField namespaces (field: FSharpField) =
  create namespaces
    <| field.FieldType
    <| field.DeclarationLocation.StartRange


let rec private findLineLens namespaces (entity: FSharpEntity) =
  [ for item in entity.MembersFunctionsAndValues do
      yield lineLensFromEntity namespaces item

    for field in entity.FSharpFields do
      yield lineLensFromField namespaces field

    for nestedEntity in entity.NestedEntities do
      yield! findLineLens namespaces nestedEntity ]


let getAll fileName check =
  match check with
  | FSharpCheckFileAnswer.Aborted ->
    []

  | FSharpCheckFileAnswer.Succeeded(check) ->
    try
      let namespaces =
        check.OpenDeclarations
          |> Seq.map (fun openDecl -> openDecl.LongId |> Seq.map (fun id -> id.idText) |> String.concat ".")
          |> Seq.append defaultNamespaces
          |> Seq.sortByDescending (fun s -> s.Length)

      check.PartialAssemblySignature.Entities
        |> Seq.filter (fun entity -> entity.DeclarationLocation.FileName = fileName)
        |> Seq.map (findLineLens namespaces)
        |> Seq.concat
        |> Seq.toList
    with exn ->
      dprintfn "Unable to create signature lenses: %s" exn.Message
      []
