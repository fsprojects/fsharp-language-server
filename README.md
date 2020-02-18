# coc-fsharp: F# Language Server for coc.nvim [![Build Status](https://dev.azure.com/v-yadli/coc-fsharp/_apis/build/status/yatli.coc-fsharp?branchName=master)](https://dev.azure.com/v-yadli/coc-fsharp/_build/latest?definitionId=3&branchName=master)

This project is an implementation of the [language server protocol](https://microsoft.github.io/language-server-protocol/) using the [F# Compiler Service](https://fsharp.github.io/FSharp.Compiler.Service/).

Original project: https://github.com/fsprojects/fsharp-language-server

# Installation
- You'll need [coc.nvim](https://github.com/neoclide/coc.nvim) installed first.
- Check `:set ft=` and if the result is not "fsharp", it doesn't trigger plugin initialization. Use something like https://github.com/sheerun/vim-polyglot
- Run `:CocInstall coc-fsharp`
