$plat = "win10-x64","linux-x64","osx.10.11-x64"

New-Item -ItemType Directory -Force -Name publish
Remove-Item publish\* -Recurse -Force
New-Item -ItemType Directory -Force publish/bin

npm install
npm run compile

Copy-Item -Path package.json publish/
Copy-Item -Path node_modules publish/
Copy-Item -Path out/client/* publish/

foreach($i in $plat) {
    dotnet publish -f netcoreapp2.0 -c Release --self-contained -r $i src/FSharpLanguageServer
    New-Item -ItemType Directory -Force publish/bin/$i
    Copy-Item -Path src/FSharpLanguageServer/bin/Release/netcoreapp2.0/$i/publish/* publish/bin/$i
}

