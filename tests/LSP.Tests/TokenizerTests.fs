module LSP.TokenizerTests

open System.IO
open System.Text
open NUnit.Framework

[<SetUp>]
let setup () = 
    LSP.Log.diagnosticsLog := stdout

[<Test>]
let ``parse content length header`` () = 
    let sample = "Content-Length: 10"
    let found = Tokenizer.parseHeader sample
    Assert.AreEqual((Tokenizer.ContentLength 10), found)

[<Test>]
let ``parse content type header`` () = 
    let sample = "Content-Type: application/vscode-jsonrpc; charset=utf-8"
    let found = Tokenizer.parseHeader sample
    Assert.AreEqual(Tokenizer.OtherHeader, found)

[<Test>]
let ``parse empty line indicating start of message`` () = 
    let found = Tokenizer.parseHeader ""
    Assert.AreEqual(Tokenizer.EmptyHeader, found)

let binaryReader (sample: string): BinaryReader = 
    let bytes = Encoding.UTF8.GetBytes(sample)
    let stream = new MemoryStream(bytes)
    new BinaryReader(stream, Encoding.UTF8)

[<Test>]
let ``take header token`` () = 
    let sample = "Line 1\r\n\
                    Line 2"
    let found = Tokenizer.readLine (binaryReader sample)
    Assert.AreEqual((Some "Line 1"), found)

[<Test>]
let ``allow newline without carriage-return`` () = 
    let sample = "Line 1\n\
                    Line 2"
    let found = Tokenizer.readLine (binaryReader sample)
    Assert.AreEqual((Some "Line 1"), found)

[<Test>]
let ``take message token`` () = 
    let sample = "{}\r\n\
                    next line..."
    let found = Tokenizer.readLength 2 (binaryReader sample)
    Assert.AreEqual("{}", found)

[<Test>]
let ``tokenize stream`` () = 
    let sample = "Content-Length: 2\r\n\
                    \r\n\
                    {}\
                    Content-Length: 1\r\n\
                    \r\n\
                    1"
    let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
    Assert.AreEqual(["{}"; "1"], found)

[<Test>]
let ``tokenize stream with multibyte characters`` () = 
    let sample = "Content-Length: 4\r\n\
                    \r\n\
                    🔥\
                    Content-Length: 4\r\n\
                    \r\n\
                    🐼"
    let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
    Assert.AreEqual(["🔥"; "🐼"], found)
