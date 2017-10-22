namespace LSP

open NUnit.Framework

module TokenizerTests =
    [<Test>]
    let ``parse content length header`` () = 
        let sample = "Content-Length: 10"
        let parsed = Tokenizer.parseHeader sample 
        Assert.That(parsed, Is.EqualTo(Tokenizer.ContentLength(10)))

    [<Test>]
    let ``parse content type header`` () = 
        let sample = "Content-Type: application/vscode-jsonrpc; charset=utf-8"
        let parsed = Tokenizer.parseHeader sample 
        Assert.That(parsed, Is.EqualTo(Tokenizer.OtherHeader))

    [<Test>]
    let ``parse empty line indicating start of message`` () = 
        Assert.That(Tokenizer.parseHeader "", Is.EqualTo(Tokenizer.EmptyHeader))

    [<Test>]
    let ``take header token`` () = 
        let sample = "Line 1\r\n\
                      Line 2"
        let chars = sample.GetEnumerator()
        Assert.That(Tokenizer.takeHeader chars, Is.EqualTo("Line 1"))

    [<Test>]
    let ``take message token`` () = 
        let sample = "{}\r\n\
                      next line..."
        let chars = sample.GetEnumerator()
        Assert.That(Tokenizer.takeMessage chars 2, Is.EqualTo("{}"))