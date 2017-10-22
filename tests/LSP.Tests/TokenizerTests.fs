namespace LSP

open NUnit.Framework

module TokenizerTests =
    [<Test>]
    let ``parse content length header`` () = 
        let sample = "Content-Length: 10"
        Assert.That(
            Tokenizer.parseHeader sample, 
            Is.EqualTo (Tokenizer.ContentLength 10))

    [<Test>]
    let ``parse content type header`` () = 
        let sample = "Content-Type: application/vscode-jsonrpc; charset=utf-8"
        Assert.That(
            Tokenizer.parseHeader sample, 
            Is.EqualTo Tokenizer.OtherHeader)

    [<Test>]
    let ``parse empty line indicating start of message`` () = 
        Assert.That(
            Tokenizer.parseHeader "", 
            Is.EqualTo Tokenizer.EmptyHeader)

    [<Test>]
    let ``take header token`` () = 
        let sample = ("Line 1\r\n\
                      Line 2".GetEnumerator())
        Assert.That(
            Tokenizer.takeHeader sample, 
            Is.EqualTo (Some "Line 1"))

    [<Test>]
    let ``take message token`` () = 
        let sample = ("{}\r\n\
                      next line...".GetEnumerator())
        Assert.That(
            Tokenizer.takeMessage sample 2, 
            Is.EqualTo "{}")

    [<Test>]
    let ``tokenize stream`` () = 
        let sample = "Content-Length: 2\r\n\
                      \r\n\
                      {}\r\n\
                      Content-Length: 1\r\n\
                      \r\n\
                      1\r\n"
        Assert.That(
            Tokenizer.tokenize sample, 
            Is.EquivalentTo ["{}"; "1"])