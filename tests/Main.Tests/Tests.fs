module Main.Tests

open System
open NUnit.Framework

[<Test>]
let ``false is true`` () = 
    Assert.IsTrue(false)