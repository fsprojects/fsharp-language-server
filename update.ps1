dotnet build -c Release
npm run compile
Copy-Item -Force .\src\FSharpLanguageServer\bin\Release\netcoreapp3.1\*.dll ~\AppData\Local\coc\extensions\coc-fsharp-data\server\
Copy-Item -Force .\src\FSharpLanguageServer\bin\Release\netcoreapp3.1\*.json ~\AppData\Local\coc\extensions\coc-fsharp-data\server\
Copy-Item -Force .\package.json ~\AppData\Local\coc\extensions\node_modules\coc-fsharp\package.json
Copy-Item -Force .\out\client\* ~\AppData\Local\coc\extensions\node_modules\coc-fsharp\out\client\
