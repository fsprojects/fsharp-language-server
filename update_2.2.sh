# !/bin/bash
plat="linux-x64"

for i in $plat
do
    dotnet publish -f netcoreapp2.2 -c Release --self-contained \
        -r $i src/FSharpLanguageServer -o ./out/server/$i
done
 
cp -r ./src/FSharpLanguageServer/out/server/linux-x64/* ~/.config/coc/extensions/node_modules/coc-fsharp/out/server/
