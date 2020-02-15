dotnet build -c Release
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp3.1/*.dll ~/.config/coc/extensions/coc-fsharp-data/server/
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp3.1/*.json ~/.config/coc/extensions/coc-fsharp-data/server/
cp ./package.json ~/.config/coc/extensions/node_modules/coc-fsharp/package.json
