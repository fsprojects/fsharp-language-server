dotnet build -c Release
Copy-Item -Force .\src\FSharpLanguageServer\bin\Release\netcoreapp2.1\* ~\AppData\Local\coc\extensions\node_modules\coc-fsharp\out\server\win10-x64\
