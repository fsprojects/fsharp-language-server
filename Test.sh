# dotnet pack src/CompactTestLogger/
# mkdir -p loggers/compact.testlogger/1.0.0
# unzip src/CompactTestLogger/bin/Debug/Compact.TestLogger.1.0.0.nupkg -d loggers/compact.testlogger/1.0.0

dotnet test \
    --test-adapter-path /Users/georgefraser/Documents/LoggerExtensions/src/NUnit.Xml.TestLogger/bin/Debug/netstandard1.5 \
    --logger compact \
    --filter 'FullyQualifiedName=ProjectCrackerTests.test that fails' \
    /Users/georgefraser/Documents/fsharp-language-server/tests/ProjectCracker.Tests/ProjectCracker.Tests.fsproj