module Main.Tests.Program

open SimpleTest
open System.Reflection

[<EntryPoint>]
let main (argv: array<string>): int =
    runAllTests(Assembly.GetExecutingAssembly())
    0