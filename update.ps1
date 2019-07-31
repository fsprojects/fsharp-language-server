dotnet build -c Release
Copy-Item -Force .\src\FSharpLanguageServer\bin\Release\netcoreapp2.2\*.dll ~\AppData\Local\coc\extensions\node_modules\coc-fsharp\out\server\
Copy-Item -Force .\src\FSharpLanguageServer\bin\Release\netcoreapp2.2\*.json ~\AppData\Local\coc\extensions\node_modules\coc-fsharp\out\server\
Copy-Item -Force .\package.json ~\AppData\Local\coc\extensions\node_modules\coc-fsharp\package.json
