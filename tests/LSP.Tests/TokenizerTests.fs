namespace LSP

open System.IO
open System.Text
open Xunit

module TokenizerTests =
    [<Fact>]
    let ``parse content length header`` () = 
        let sample = "Content-Length: 10"
        Assert.Equal(
            Tokenizer.parseHeader sample, 
            (Tokenizer.ContentLength 10))

    [<Fact>]
    let ``parse content type header`` () = 
        let sample = "Content-Type: application/vscode-jsonrpc; charset=utf-8"
        Assert.Equal(
            Tokenizer.parseHeader sample, 
            Tokenizer.OtherHeader)

    [<Fact>]
    let ``parse empty line indicating start of message`` () = 
        Assert.Equal(
            Tokenizer.parseHeader "", 
            Tokenizer.EmptyHeader)

    let binaryReader (sample: string): BinaryReader = 
        let bytes = Encoding.UTF8.GetBytes(sample)
        let stream = new MemoryStream(bytes)
        new BinaryReader(stream, Encoding.UTF8)

    [<Fact>]
    let ``take header token`` () = 
        let sample = "Line 1\r\n\
                      Line 2"
        Assert.Equal(
            Tokenizer.readLine (binaryReader sample), 
            (Some "Line 1"))

    [<Fact>]
    let ``allow newline without carriage-return`` () = 
        let sample = "Line 1\n\
                      Line 2"
        Assert.Equal(
            Tokenizer.readLine (binaryReader sample), 
            (Some "Line 1"))

    [<Fact>]
    let ``take message token`` () = 
        let sample = "{}\r\n\
                      next line..."
        Assert.Equal(
            Tokenizer.readLength 2 (binaryReader sample),
            "{}")

    [<Fact>]
    let ``tokenize stream`` () = 
        let sample = "Content-Length: 2\r\n\
                      \r\n\
                      {}\
                      Content-Length: 1\r\n\
                      \r\n\
                      1"
        let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
        Assert.True(["{}"; "1"] = found)

    [<Fact>]
    let ``tokenize stream with multibyte characters`` () = 
        let sample = "Content-Length: 4\r\n\
                      \r\n\
                      🔥\
                      Content-Length: 4\r\n\
                      \r\n\
                      🐼"
        let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
        Assert.True(["🔥"; "🐼"] = found)
    