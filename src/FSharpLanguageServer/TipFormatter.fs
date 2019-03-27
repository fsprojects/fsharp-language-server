module FSharpLanguageServer.TipFormatter

open System
open System.IO
open System.Text.RegularExpressions
open System.Collections.Generic
open FSharp.Compiler.SourceCodeServices
open LSP.Types
open LSP.Log
open HtmlAgilityPack

do HtmlAgilityPack.HtmlNode.ElementsFlags.Remove("param") |> ignore

// Based on https://github.com/dotnet/roslyn/blob/master/src/Workspaces/Core/Portable/Utilities/Documentation/XmlDocumentationProvider.cs

type private CachedMember = {
    summary: string option
    // For example, ("myParam", "A great param.")
    parameters: (string * string) list
    returns: string option
    // For example, ("T:System.ArgumentNullException", "The property is being set to null.")
    exceptions: (string * string) list 
}

type private CachedFile = {
    members: Dictionary<string, CachedMember>
    loadTime: DateTime
}

let private cache = Dictionary<string, CachedFile>()

let private convertTag(node: HtmlNode, tag: string, mapper: HtmlNode -> HtmlNode) = 
    let nodes = List.ofSeq(node.Descendants(tag)) 
    for n in nodes do 
        let parent = n.ParentNode
        let r = mapper(n)
        parent.ReplaceChild(r, n) |> ignore 

let private lastWord(text: string): string = 
    let wordPattern = Regex(@"[a-zA-Z]\w*")
    let words = [for m in wordPattern.Matches(text) do yield m.Value]
    let maybeLast = List.tryLast(words)
    Option.defaultValue "" maybeLast

let private convertSee(n: HtmlNode): HtmlNode = 
    let ref = n.GetAttributeValue("cref", n.InnerText)
    let name = lastWord(ref)
    HtmlNode.CreateNode("`" + name + "`")

let private convertNameToCode(n: HtmlNode): HtmlNode = 
    let name = n.GetAttributeValue("name", n.InnerText)
    HtmlNode.CreateNode("`" + name + "`")

let private convertInnerTextToCode(n: HtmlNode): HtmlNode = 
    let text = n.InnerText
    HtmlNode.CreateNode("`" + text + "`")

let private convertPara(n: HtmlNode): HtmlNode = 
    HtmlNode.CreateNode("\n\n")

/// Convert special tags listed in https://docs.microsoft.com/en-us/dotnet/csharp/codedoc to markdown
let private convertSpecialTagsToMarkdown(node: HtmlNode) = 
    convertTag(node, "see", convertSee)
    convertTag(node, "paramref", convertNameToCode)
    convertTag(node, "typeparamref", convertNameToCode)
    convertTag(node, "c", convertInnerTextToCode)
    convertTag(node, "code", convertInnerTextToCode)
    convertTag(node, "para", convertPara)

let private cref(node: HtmlNode) =
    let attr = node.GetAttributeValue("cref", "")
    let parts = attr.Split(':')
    Array.last(parts)

let private ensure(xmlFile: FileInfo) = 
    if xmlFile.Exists then 
        let needsUpdate = 
            match cache.TryGetValue(xmlFile.FullName) with 
            | false, _ -> true
            | _, existing -> existing.loadTime < xmlFile.LastWriteTime
        if needsUpdate then
            dprintfn "Reading %s" xmlFile.FullName
            let parsed = Dictionary<string, CachedMember>()
            // The extension of these files is .xml, but they seem to actually be HTML
            // For example, they contain unclosed <p> tags
            let html = new HtmlDocument()
            html.Load(xmlFile.FullName)
            // Find all members
            for m in html.DocumentNode.Descendants("member") do 
                convertSpecialTagsToMarkdown(m)
                let name = m.GetAttributeValue("name", "")
                let summary = [for e in m.Descendants("summary") do yield e.InnerHtml]
                let parameters = [for e in m.Descendants("param") do yield e.GetAttributeValue("name", ""), e.InnerHtml]
                let returns = [for e in m.Descendants("returns") do yield e.InnerHtml]
                let exceptions = [for e in m.Descendants("exception") do yield cref(e), e.InnerHtml]
                parsed.TryAdd(name, {
                    summary = List.tryHead(summary)
                    parameters = parameters 
                    returns = List.tryHead(returns) 
                    exceptions = exceptions
                }) |> ignore
                cache.TryAdd(xmlFile.FullName, {
                    members = parsed 
                    loadTime = xmlFile.LastWriteTime
                }) |> ignore

/// Find the documentation for `memberName` inside of `xmlFile`
let private find(xmlFile: FileInfo, memberName: string): CachedMember option = 
    ensure(xmlFile)
    match cache.TryGetValue(xmlFile.FullName) with 
    | false, _ -> None 
    | _, cached -> 
        match cached.members.TryGetValue(memberName) with 
        | false, _ -> None 
        | _, m -> Some(m)

/// Render complete documentation, including parameter and return information, for a member that has no overloads
let docComment(doc: FSharpXmlDoc): string option =
    match doc with
    | FSharpXmlDoc.None -> None
    | FSharpXmlDoc.Text(s) -> Some(s)
    | FSharpXmlDoc.XmlDocFileSignature(dllPath, memberName) ->
        let xmlFile = FileInfo(Path.ChangeExtension(dllPath, ".xml"))
        match find(xmlFile, memberName) with 
        | None -> None 
        | Some(m) -> 
            let lines = [
                if m.summary.IsSome && m.summary.Value.Length > 0 then 
                    yield m.summary.Value.Trim()
                for name, desc in m.parameters do 
                    yield sprintf "**%s** %s" name desc 
                if m.returns.IsSome && m.returns.Value.Length > 0 then 
                    yield sprintf "**returns** %s" m.returns.Value
                for name, desc in m.exceptions do 
                    yield sprintf "**exception** `%s` %s" name desc ]
            let comment = String.concat "\n\n" lines
            Some(comment)

/// Render just the summary documentation
let docSummaryOnly(doc: FSharpXmlDoc): string option = 
    match doc with
    | FSharpXmlDoc.None -> None
    | FSharpXmlDoc.Text(s) -> Some(s)
    | FSharpXmlDoc.XmlDocFileSignature(dllPath, memberName) ->
        let xmlFile = FileInfo(Path.ChangeExtension(dllPath, ".xml"))
        match find(xmlFile, memberName) with 
        | None -> None 
        | Some(m) -> m.summary |> Option.map (fun(s) -> s.Trim())

/// Render documentation for an overloaded member
let private overloadComment(docs: FSharpXmlDoc list): string option = 
    let summaries = [
        for doc in docs do 
            match doc with 
            | FSharpXmlDoc.None -> ()
            | FSharpXmlDoc.Text(s) -> yield s
            | FSharpXmlDoc.XmlDocFileSignature(dllPath, memberName) -> 
                let xmlFile = FileInfo(Path.ChangeExtension(dllPath, ".xml"))
                match find(xmlFile, memberName) with 
                | None -> () 
                | Some(m) -> 
                    match m.summary with 
                    | None -> () 
                    | Some(message) -> yield message.Trim() ]
    let first = List.tryHead summaries 
    match first with 
    | None -> None
    | Some(summary) -> Some(sprintf "%s\n\n*(%d overloads)*" summary docs.Length)

let private markup(s: string): MarkupContent = 
    {
        kind=MarkupKind.Markdown
        value=s
    }

/// Add documentation information to the inline help of autocomplete
let resolveDocs(item: CompletionItem, candidate: FSharpDeclarationListItem): Async<CompletionItem> = 
    async {
        let! FSharpToolTipText(xs) = candidate.DescriptionTextAsync
        // FSharpToolTipText is weirdly nested, unwrap the parts that point to documentation
        let docs = [ 
            for x in xs do 
                match x with 
                | FSharpToolTipElement.Group(ys) -> 
                    for y in ys do 
                        yield y.XmlDoc
                | _ -> () ]
        // Render docs differently depending on how many overloads we find
        match docs with 
        | [] -> return item 
        | [one] -> 
            let value = docComment(one)
            let doc = Option.map markup value
            return {item with documentation=doc}
        | many -> 
            let value = overloadComment(many)
            let doc = Option.map markup value
            return {item with documentation=doc}
    }