namespace LSP

open NUnit.Framework

module ParserTests =
    [<Test>]
    let ``parse content length header`` () = 
        let sample = "Content-Length: 10"
        let parsed = Parser.parseHeader sample 
        Assert.That(parsed, Is.EqualTo(Parser.ContentLength(10)))

    [<Test>]
    let ``parse content type header`` () = 
        let sample = "Content-Type: application/vscode-jsonrpc; charset=utf-8"
        let parsed = Parser.parseHeader sample 
        Assert.That(parsed, Is.EqualTo(Parser.Other))