module FSharpLanguageServer.Config 
open FSharp.Data

type FSharpLanguageServerConfig = JsonProvider<"""
[{
      "fsharp": {
        "trace": {
          "server": "off"
        },
        "project": {
          "define": [ "USE_SOME_FLAG" ],
          "includeCompileBefore": false,
          "otherFlags": [ "--some-compiler-flags" ]
        },
        "codelens": {
          "references": true
        },
        "analysis": {
          "unusedDeclaration": true
        }
      }
}]""", SampleIsList=true>
