# F# Language Server
This project is an implementation of the [language server protocol](https://microsoft.github.io/language-server-protocol/) using the [F# Compiler Service](https://fsharp.github.io/FSharp.Compiler.Service/).

## Features

### Hover
![Hover](videos/Hover.mov.gif)

### Autocomplete
![Autocomplete](videos/Autocomplete.mov.gif)

### Method signature help
![Signature help](videos/SignatureHelp.mov.gif)

### Find symbols in document
![Document symbols](videos/DocumentSymbols.mov.gif)

### Find symbols in workspace
![Workspace symbols](videos/WorkspaceSymbols.mov.gif)

### Go-to-definition
![Go to definition](videos/GoToDefinition.mov.gif)

### Find references
![Find references](videos/FindReferences.mov.gif)

### Rename symbol
![Rename symbol](videos/RenameSymbol.mov.gif)

### Show errors on save
![Show errors](videos/ShowErrors.mov.gif)

## Code structure
The language server protocol (LSP) is very similar to the API defined by the F# compiler service (FCS); most of the implementation is devoted to translating between the types used by FCS and the JSON representation of LSP.

- src/LSP: Server-side implementation of [language server protocol](https://microsoft.github.io/language-server-protocol/specification)
- src/ProjectCracker: Figures out [F# compiler options](https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/compiler-options) using [Buildalyzer](https://github.com/daveaglick/Buildalyzer) and the MSBuild API.
- src/FSharpLanguageServer: F# language server
- tests/LSP.Tests
- tests/ProjectCracker.Tests
- tests/FSharpLanguageServer.Tests
- sample: Example projects used by tests

## How is this project different than [Ionide](https://github.com/ionide)?
Ionide is a suite of F# plugins for VSCode; F# language server is analagous to the [FSAC](https://github.com/fsharp/FsAutoComplete) component. While FSAC is based on a custom JSON protocol; F#LS is based on the [language server protocol](https://microsoft.github.io/language-server-protocol/specification) standard. 

The implementation is a thin wrapper around [F# Compiler Service](https://fsharp.github.io/FSharp.Compiler.Service/) and is heavily focused on performance. For example, autocompleting in medium-sized file in F# Language Server (left) and Ionide (right):

![Autocomplete warm](videos/LSP-vs-Ionide-Warm.gif)

