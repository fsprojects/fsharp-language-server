dotnet build -c Release
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp2.2/*.dll ~/.config/coc/extensions/node_modules/coc-fsharp/out/server/linux-x64/
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp2.2/*.dll ~/.config/coc/extensions/node_modules/coc-fsharp/out/server/osx.10.11-x64/
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp2.2/*.json ~/.config/coc/extensions/node_modules/coc-fsharp/out/server/linux-x64/
cp ./src/FSharpLanguageServer/bin/Release/netcoreapp2.2/*.json ~/.config/coc/extensions/node_modules/coc-fsharp/out/server/osx.10.11-x64/
cp ./package.json ~/.config/coc/extensions/node_modules/coc-fsharp/package.json
