## Structure
- src/LSP.Json: Forked from FSharp.Data to work around https://github.com/Microsoft/visualfsharp/issues/3303
- src/LSP: Server-side implementation of [language server protocol](https://github.com/Microsoft/language-server-protocol/blob/master/protocol.md)
- src/Main: F# language server
- src/SimpleTest: Simple test runner because I couldn't get nunit or xunit to stop messing with stderr
- tests/LSP.Tests
- tests/Main.Tests