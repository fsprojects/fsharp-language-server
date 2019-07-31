dotnet build -c Release
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp2.2/*.dll ~/.config/coc/extensions/node_modules/coc-fsharp/out/server/
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp2.2/*.json ~/.config/coc/extensions/node_modules/coc-fsharp/out/server/
cp ./package.json ~/.config/coc/extensions/node_modules/coc-fsharp/package.json
