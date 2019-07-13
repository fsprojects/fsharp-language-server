# !/bin/bash
plat="win10-x64 linux-x64 osx.10.11-x64"

rm -rf ./out
mkdir -p ./out/server

npm install
npm run compile

for i in $plat
do
    dotnet publish -f netcoreapp2.2 -c Release --self-contained \
        -r $i src/FSharpLanguageServer -o ../out/server/$i # arbitrary change of output path?
done
 
npm pack
