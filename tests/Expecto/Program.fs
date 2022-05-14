module FSharpLanguageServer.Tests.Main
open Expecto


[<Tests>]
let mainTests=
    
    testSequenced<|testList "All" [
        testSequenced<|testList "LanguageServer" [
            ServerTests.serverTests
            ProjectManagerTests.tests
            FormattingTests.tests
        ] 
        testList "ProjectCracker" [
            ProjectCrackerTests.crackingTests
            ProjectCrackerTests.tests2
        ] 
        testList "LSPTests" [
            LSP.Tests.DocumentStoreTests.tests
            LSP.Tests.JsonTests.tests
            LSP.Tests.JsonTests.tests2
            LSP.Tests.JsonTests.tests3
            LSP.Tests.JsonTests.tests4
            LSP.Tests.LanguageServerTests.tests
            LSP.Tests.LanguageServerTests.tests2
            LSP.Tests.ParserTests.tests
            LSP.Tests.TokenizerTests.tests
        ] 
    ]


[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args


