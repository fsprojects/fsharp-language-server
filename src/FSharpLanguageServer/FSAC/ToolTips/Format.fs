module private FSharpLanguageServer.ToolTips.Formatting

open System
open System.Text.RegularExpressions

let inline nl<'T> = Environment.NewLine

let tagPattern (tagName: string) =
    sprintf
        """(?'void_element'<%s(?'void_attributes'\s+[^\/>]+)?\/>)|(?'non_void_element'<%s(?'non_void_attributes'\s+[^>]+)?>(?'non_void_innerText'(?:(?!<%s>)(?!<\/%s>)[\s\S])*)<\/%s\s*>)"""
        tagName
        tagName
        tagName
        tagName
        tagName

type TagInfo =
    | VoidElement of attributes: Map<string, string>
    | NonVoidElement of innerText: string * attributes: Map<string, string>

type FormatterInfo =
    { TagName: string
      Formatter: TagInfo -> string option }

let private extractTextFromQuote (quotedText: string) =
    quotedText.Substring(1, quotedText.Length - 2)


let extractMemberText (text: string) =
    let pattern =
        "(?'member_type'[a-z]{1}:)?(?'member_text'.*)"

    let m =
        Regex.Match(text, pattern, RegexOptions.IgnoreCase)

    if m.Groups.["member_text"].Success then
        m.Groups.["member_text"].Value
    else
        text

let getAttributes (attributes: Group) =
    if attributes.Success then
        let pattern =
            """(?'key'\S+)=(?'value''[^']*'|"[^"]*")"""

        Regex.Matches(attributes.Value, pattern, RegexOptions.IgnoreCase)
        |> Seq.cast<Match>
        |> Seq.map (fun m -> m.Groups.["key"].Value, extractTextFromQuote m.Groups.["value"].Value)
        |> Map.ofSeq
    else
        Map.empty

type AttrLookup = Map<string, string> -> Option<string>

let cref: AttrLookup = Map.tryFind "cref"
let langword: AttrLookup = Map.tryFind "langword"
let href: AttrLookup = Map.tryFind "href"
let lang: AttrLookup = Map.tryFind "lang"
let name: AttrLookup = Map.tryFind "name"

let rec applyFormatter (info: FormatterInfo) text =
    let pattern = tagPattern info.TagName

    match Regex.Match(text, pattern, RegexOptions.IgnoreCase) with
    | m when m.Success ->
        if m.Groups.["void_element"].Success then
            let attributes =
                getAttributes m.Groups.["void_attributes"]

            let replacement = VoidElement attributes |> info.Formatter

            match replacement with
            | Some replacement ->
                text.Replace(m.Groups.["void_element"].Value, replacement)
                // Re-apply the formatter, because perhaps there is more
                // of the current tag to convert
                |> applyFormatter info

            | None ->
                // The formatter wasn't able to convert the tag
                // Return as it is and don't re-apply the formatter
                // otherwise it will create an infinity loop
                text

        else if m.Groups.["non_void_element"].Success then
            let innerText = m.Groups.["non_void_innerText"].Value

            let attributes =
                getAttributes m.Groups.["non_void_attributes"]

            let replacement =
                NonVoidElement(innerText, attributes)
                |> info.Formatter

            match replacement with
            | Some replacement ->
                // Re-apply the formatter, because perhaps there is more
                // of the current tag to convert
                text.Replace(m.Groups.["non_void_element"].Value, replacement)
                |> applyFormatter info

            | None ->
                // The formatter wasn't able to convert the tag
                // Return as it is and don't re-apply the formatter
                // otherwise it will create an infinity loop
                text
        else
            // Should not happend but like that we are sure to handle all possible cases
            text
    | _ -> text

let codeBlock =
    { TagName = "code"
      Formatter =
        function
        | VoidElement _ -> None

        | NonVoidElement (innerText, attributes) ->
            let lang =
                match lang attributes with
                | Some lang -> lang

                | None -> "forceNoHighlight"

            let formattedText =
                if innerText.Contains("\n") then

                    if innerText.StartsWith("\n") then

                        sprintf "```%s%s\n```" lang innerText

                    else
                        sprintf "```%s\n%s\n```" lang innerText

                else
                    sprintf "`%s`" innerText

            Some formattedText

    }
    |> applyFormatter

let codeInline =
    { TagName = "c"
      Formatter =
        function
        | VoidElement _ -> None
        | NonVoidElement (innerText, _) -> "`" + innerText + "`" |> Some }
    |> applyFormatter

let link text uri = $"[`%s{text}`](%s{uri})"
let code text = $"`%s{text}`"

let anchor =
    { TagName = "a"
      Formatter =
        function
        | VoidElement attributes ->
            match href attributes with
            | Some href -> Some(link href href)
            | None -> None

        | NonVoidElement (innerText, attributes) ->
            match href attributes with
            | Some href -> Some(link innerText href)
            | None -> Some(code innerText) }
    |> applyFormatter

let paragraph =
    { TagName = "para"
      Formatter =
        function
        | VoidElement _ -> None

        | NonVoidElement (innerText, _) -> nl + innerText + nl |> Some }
    |> applyFormatter

let block =
    { TagName = "block"
      Formatter =
        function
        | VoidElement _ -> None

        | NonVoidElement (innerText, _) -> nl + innerText + nl |> Some }
    |> applyFormatter

let see =
    let formatFromAttributes (attrs: Map<string, string>) =
        match cref attrs with
        // crefs can have backticks in them, which mess with formatting.
        // for safety we can just double-backtick and markdown is ok with that.
        | Some cref -> Some $"``{extractMemberText cref}``"
        | None ->
            match langword attrs with
            | Some langword -> Some(code langword)
            | None -> None

    { TagName = "see"
      Formatter =
        function
        | VoidElement attributes -> formatFromAttributes attributes
        | NonVoidElement (innerText, attributes) ->
            if String.IsNullOrWhiteSpace innerText then
                formatFromAttributes attributes
            else
                match href attributes with
                | Some externalUrl -> Some(link innerText externalUrl)
                | None -> Some $"`{innerText}`" }
    |> applyFormatter

let xref =
    { TagName = "xref"
      Formatter =
        function
        | VoidElement attributes ->
            match href attributes with
            | Some href -> Some(link href href)
            | None -> None

        | NonVoidElement (innerText, attributes) ->
            if String.IsNullOrWhiteSpace innerText then
                match href attributes with
                | Some href -> Some(link innerText href)
                | None -> None
            else
                Some(code innerText) }
    |> applyFormatter

let paramRef =
    { TagName = "paramref"
      Formatter =
        function
        | VoidElement attributes ->
            match name attributes with
            | Some name -> Some(code name)
            | None -> None

        | NonVoidElement (innerText, attributes) ->
            if String.IsNullOrWhiteSpace innerText then
                match name attributes with
                | Some name ->
                    // TODO: Add config to generates command
                    Some(code name)
                | None -> None
            else
                Some(code innerText)

    }
    |> applyFormatter

let typeParamRef =
    { TagName = "typeparamref"
      Formatter =
        function
        | VoidElement attributes ->
            match name attributes with
            | Some name -> Some(code name)
            | None -> None

        | NonVoidElement (innerText, attributes) ->
            if String.IsNullOrWhiteSpace innerText then
                match name attributes with
                | Some name ->
                    // TODO: Add config to generates command
                    Some(code name)
                | None -> None
            else
                Some(code innerText) }
    |> applyFormatter

let fixPortableClassLibrary (text: string) =
    text.Replace(
        "~/docs/standard/cross-platform/cross-platform-development-with-the-portable-class-library.md",
        "https://docs.microsoft.com/en-gb/dotnet/standard/cross-platform/cross-platform-development-with-the-portable-class-library"
    )
