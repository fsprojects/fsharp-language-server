module LSP.Tests

open NUnit.Framework

[<Test>]
let ``say hello`` () = 
    let helloGeorge = Say.hello "George"
    Assert.That(helloGeorge, Is.EqualTo("Hello George"))
