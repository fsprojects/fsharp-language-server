namespace LSP

open System.IO
open System.Text
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

    let binaryReader (sample: string): BinaryReader = 
        let bytes = Encoding.UTF8.GetBytes(sample)
        let stream = new MemoryStream(bytes)
        new BinaryReader(stream, Encoding.UTF8)

    [<Test>]
    let ``take header token`` () = 
        let sample = "Line 1\r\n\
                      Line 2"
        Assert.That(
            Tokenizer.readLine (binaryReader sample), 
            Is.EqualTo (Some "Line 1"))

    [<Test>]
    let ``allow newline without carriage-return`` () = 
        let sample = "Line 1\n\
                      Line 2"
        Assert.That(
            Tokenizer.readLine (binaryReader sample), 
            Is.EqualTo (Some "Line 1"))

    [<Test>]
    let ``take message token`` () = 
        let sample = "{}\r\n\
                      next line..."
        Assert.That(
            Tokenizer.readLength 2 (binaryReader sample),
            Is.EqualTo "{}")

    [<Test>]
    let ``tokenize stream`` () = 
        let sample = "Content-Length: 2\r\n\
                      \r\n\
                      {}\
                      Content-Length: 1\r\n\
                      \r\n\
                      1"
        Assert.That(
            Tokenizer.tokenize (binaryReader sample), 
            Is.EquivalentTo ["{}"; "1"])

    [<Test>]
    let ``tokenize stream with multibyte characters`` () = 
        let sample = "Content-Length: 4\r\n\
                      \r\n\
                      🔥\
                      Content-Length: 4\r\n\
                      \r\n\
                      🐼"
        Assert.That(
            Tokenizer.tokenize (binaryReader sample), 
            Is.EquivalentTo ["🔥"; "🐼"])
    