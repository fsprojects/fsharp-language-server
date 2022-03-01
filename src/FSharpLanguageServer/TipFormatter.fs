module FSharpLanguageServer.TipFormatter

open System
open System.IO
open System.Text.RegularExpressions
open System.Collections.Generic
open FSharp.Compiler.EditorServices
open LSP.Types
open LSP.Log
open HtmlAgilityPack
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open FSharpLanguageServer
do HtmlAgilityPack.HtmlNode.ElementsFlags.Remove("param") |> ignore

// Based on https://github.com/dotnet/roslyn/blob/master/src/Workspaces/Core/Portable/Utilities/Documentation/XmlDocumentationProvider.cs

//Eli- Tagged text formatting from straight from fsac
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
    | TextTag.Property
    | TextTag.Space
    | TextTag.StringLiteral
    | TextTag.Text
    | TextTag.Parameter
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
    | TextTag.TypeParameter -> $"{t.Text}"
///Adjusted to have a newline before function params
let formatTaggedFunctionText (t: TaggedText) : string =
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
    | TextTag.TypeParameter -> $"{t.Text}"
    | TextTag.Parameter-> $"\n    {t.Text}" //We use 4 spaces becuase tabs look way to big in completion popups
    
    ///How to perform formatting:
    //When the type is a function:
    //Each paramater should have a \n\t before it to create seperation
    //The last occurance of -> should have a \nt\t before it as well

let formatTaggedTexts (t:TaggedText[])= 
    //We check positions where we would exepect to find the name of the function in a  fucntion defintion eg: let map etc..  |0"let"|1" "|2"map"| etc..
    // or let rec map or let rec private map
    let functionTag=
        [t|>Array.tryItem(2);t|>Array.tryItem(4);t|>Array.tryItem(6)]
        |>List.tryFind
            (Option.map(fun text->text.Tag=TextTag.Function)
            >>Option.defaultValue false)
        
    match functionTag  with
    |Some(_) ->
         //We find the last ->  and insert an extra tag to create a newline before the return type of the function
        let index=t|>Array.tryFindIndexBack(fun tag->tag.Text="->") 
        let arr=
            match index with 
            |Some(i)-> t|>Array.insertAt i (TaggedText(TextTag.Punctuation,"\n    "))
            |_->t

        arr|>(Array.map formatTaggedFunctionText >> String.concat "") 
    |_-> t|>(Array.map formatTaggedText >> String.concat "" ) 
    

    
    

let formatGenericParameters (typeMappings: TaggedText [] list) =
    typeMappings
    |> List.map (fun typeMap -> $"* {formatTaggedTexts typeMap}")
    |> String.concat Environment.NewLine


type CachedMember = {
    summary: string option
    // For example, ("myParam", "A great param.")
    parameters: (string * string) list
    returns: string option
    // For example, ("T:System.ArgumentNullException", "The property is being set to null.")
    exceptions: (string * string) list
    text:string option
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
let parseHtml(node)=
    convertSpecialTagsToMarkdown(node)
    let summary = [for e in node.Descendants("summary") do yield e.InnerHtml]
    let parameters = [for e in node.Descendants("param") do yield e.GetAttributeValue("name", ""), e.InnerHtml]
    let returns = [for e in node.Descendants("returns") do yield e.InnerHtml]
    let exceptions = [for e in node.Descendants("exception") do yield cref(e), e.InnerHtml]
    let text= [for e in node.Descendants("#text") do yield e.InnerHtml]
    {
        summary = List.tryHead(summary)
        parameters = parameters
        returns = List.tryHead(returns)
        exceptions = exceptions
        text=List.tryHead(text)
    }
///Generates tooltip info from a parsed html data object
let createCommentFromParsed data=
    let lines = [
        if data.summary.IsSome && data.summary.Value.Length > 0 then
            yield data.summary.Value.Trim()
        if data.text.IsSome && data.text.Value.Length > 0 then
            yield data.text.Value.Trim()
        for name, desc in data.parameters do
            yield sprintf "**%s** %s" name desc
        if data.returns.IsSome && data.returns.Value.Length > 0 then
            yield sprintf "**returns** %s" data.returns.Value
        for name, desc in data.exceptions do
            yield sprintf "**exception** `%s` %s" name desc ]
    let comment = String.concat "\n\n" lines
    Some(comment)

let private ensure(docFile: FileInfo) =
    let file=
        match docFile.Exists with
        |true->Some(docFile)
        |false->
            //This should put us around the nuget libraries directory
            //TODO: this really needs a check to make sure the version is the same between the xml file found and the one it was looking for. 
            //I'll need to investigate nuget package structure conscistency to be able to do this
            docFile.Directory.Parent.Parent.GetFiles(docFile.Name,EnumerationOptions(RecurseSubdirectories=true))//TODO This could get very expensive on larger projects. it would eb good to find a better solution
            |>Array.tryHead
            
    match file with 
    |None->(docFile)
    |Some(xmlFile)->
        let needsUpdate =
            match cache.TryGetValue(xmlFile.FullName) with
            | false, _ -> true
            | _, existing -> existing.loadTime < xmlFile.LastWriteTime
        if needsUpdate then
            lgInfo "Reading {file}" xmlFile.FullName
            let parsed = Dictionary<string, CachedMember>()
            // The extension of these files is .xml, but they seem to actually be HTML
            // For example, they contain unclosed <p> tags
            let html = new HtmlDocument()
            html.Load(xmlFile.FullName)
            lgVerb "loaded xml for {file}" xmlFile.FullName
            // Find all members
            for m in html.DocumentNode.Descendants("member") do
                let name = m.GetAttributeValue("name", "")
                parsed.TryAdd(name,m|>parseHtml) |> ignore
                cache.TryAdd(xmlFile.FullName, {
                    members = parsed
                    loadTime = xmlFile.LastWriteTime
                }) |> ignore
            lgVerb "Parsed members for {file}" xmlFile.FullName
        xmlFile
        
    
    

/// Find the documentation for `memberName` inside of `xmlFile`
let private find(xmlFile: FileInfo, memberName: string): CachedMember option =
    let newFile= ensure(xmlFile)
    match cache.TryGetValue(newFile.FullName) with
    | false, _ -> None
    | _, cached ->
        match cached.members.TryGetValue(memberName) with
        | false, _ -> None
        | _, m -> Some(m)

/// Render complete documentation, including parameter and return information, for a member that has no overloads
let docComment(doc: FSharpXmlDoc): string option =
    match doc with
    | FSharpXmlDoc.None -> None
    | FSharpXmlDoc.FromXmlText(xml) ->
        let htmlDoc=HtmlDocument()  //TODO: it would be good to memoize this to stop it from being recalculated all the time
        htmlDoc.LoadHtml(xml.UnprocessedLines|> String.concat "\n")
        htmlDoc.DocumentNode
        |>parseHtml
        |>createCommentFromParsed
    | FSharpXmlDoc.FromXmlFile(dllPath, memberName) ->
        let xmlFile = FileInfo(Path.ChangeExtension(dllPath, ".xml"))
        match find(xmlFile, memberName) with
        | None -> None
        | Some(m) ->
            m|>createCommentFromParsed 

/// Render just the summary documentation
let docSummaryOnly(doc: FSharpXmlDoc): string option =
    match doc with
    | FSharpXmlDoc.None -> None
    | FSharpXmlDoc.FromXmlText(xml) ->
        xml.UnprocessedLines
        |> String.concat "\n"
        |> Some //TODO: make this use parsed data
    | FSharpXmlDoc.FromXmlFile(dllPath, memberName) ->
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
//            | FSharpXmlDoc.Text(s) -> yield s
            | FSharpXmlDoc.FromXmlText(xml) ->
                yield xml.UnprocessedLines
                        |> String.concat "\n"
            | FSharpXmlDoc.FromXmlFile(dllPath, memberName) ->
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

///Takes a function signature and returns a string with the definition and type signature
///returns a string with the definition and type signature
let private extractSignature (ToolTipText tips) =
    let firstResult x =
        match x with
        | ToolTipElement.Group gs -> List.tryPick (fun (t : ToolTipElementData) -> if not (t.MainDescription.Length=0) then Some (t.MainDescription |>formatTaggedTexts)else None) gs
        | _ -> None
    tips
    |> Seq.tryPick firstResult
    |> Option.defaultValue ""
/// Add documentation information to the inline help of autocomplete
let resolveDocs(item: CompletionItem, candidate: DeclarationListItem): Async<CompletionItem> =
    async {
        
        let text=candidate.Description
        let elems= match text with ToolTipText(a)->a
        let signature= extractSignature(text)
        // ToolTipText is weirdly nested, unwrap the parts that point to documentation
        let docs = [ 
            for x in elems do 
                match x with 
                | ToolTipElement.Group(ys) -> 
                    for y in ys do 
                        yield y.XmlDoc
                | _ -> () ]
        // Render docs differently depending on how many overloads we find
        match docs with 
        | [] -> return {item with detail=Some signature;  } 
        | [one] -> 
            let value = docComment(one)
            let doc = Option.map markup value
            return {item with documentation=doc;detail=Some signature}
        | many -> 
            let value = overloadComment(many)
            let doc = Option.map markup value
            return {item with documentation=doc;detail=Some signature}
    }
    
