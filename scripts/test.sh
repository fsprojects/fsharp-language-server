dotnet build sample/Sample.fsproj
dotnet run -p tests/LSP.Tests
dotnet run -p tests/Main.Tests

# To run a single test: dotnet run -p tests/Main.Tests -- "write the name of the test function here"