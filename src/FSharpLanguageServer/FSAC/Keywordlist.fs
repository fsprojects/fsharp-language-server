namespace FsAutoComplete
open LSP.Types
open LSP.BaseTypes
open FSharp.Compiler.Text
open FSharp.Compiler.Tokenization
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Symbols

module KeywordList =
    open FSharp.Data

    let keywordDescriptions = FSharpKeywords.KeywordsWithDescription |> dict

    let keywordTooltips =
        keywordDescriptions
        |> Seq.map (fun kv ->
        let lines = kv.Value.Replace("\r\n", "\n").Split('\n')

        let allLines =
            Array.concat [| [| "<summary>" |]
                            lines
                            [| "</summary>" |] |]

        let tip =
            ToolTipText [ ToolTipElement.Single(
                            [| TaggedText.tagText kv.Key |],
                            FSharpXmlDoc.FromXmlText(FSharp.Compiler.Xml.XmlDoc(allLines, Range.Zero))
                        ) ]

        kv.Key, tip)
        |> dict

    let hashDirectives =
        [   
            "r", "References an assembly"
            "load", "Reads a source file, compiles it, and runs it."
            "I", "Specifies an assembly search path in quotation marks."
            "light", "Enables or disables lightweight syntax, for compatibility with other versions of ML"
            "if", "Supports conditional compilation"
            "else", "Supports conditional compilation"
            "endif", "Supports conditional compilation"
            "nowarn", "Disables a compiler warning or warnings"
            "line", "Indicates the original source code line" 
        ]
        |> dict

    let hashSymbolCompletionItems =
        hashDirectives
        |> Seq.map (fun kv ->
        { defaultCompletionItem with
            detail=Some kv.Key
            kind = Some CompletionItemKind.Keyword
            insertText = Some kv.Key
            filterText = Some kv.Key
            sortText = Some kv.Key
            documentation = Some( {kind=MarkupKind.Markdown;value=kv.Value})
            label = "#" + kv.Key })
        |> Seq.toArray 

    let allKeywords: string list =
        keywordDescriptions
        |> Seq.map ((|KeyValue|) >> fst)
        |> Seq.toList

    let keywordCompletionItems =
        allKeywords
        |> List.mapi (fun id k ->
        {defaultCompletionItem with  
                label = k
                detail= Some k
                data= JsonValue.Record [|"FullName", JsonValue.String(k)|]
                CompletionItem.kind = Some CompletionItemKind.Keyword
                documentation =Some ({kind=MarkupKind.Markdown;value=keywordDescriptions[k]})
                sortText = None//Some(sprintf "1000000%d" id)
                filterText = Some k
                insertText = Some k
            })
        
