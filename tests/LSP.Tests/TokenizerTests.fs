module LSP.TokenizerTests

open System.IO
open System.Text
open SimpleTest

let ``test parse content length header`` (t: TestContext) = 
    let sample = "Content-Length: 10"
    let found = Tokenizer.parseHeader sample
    if found <> (Tokenizer.ContentLength 10) then Fail(found)

let ``test parse content type header`` (t: TestContext) = 
    let sample = "Content-Type: application/vscode-jsonrpc; charset=utf-8"
    let found = Tokenizer.parseHeader sample
    if found <> Tokenizer.OtherHeader then Fail(found)

let ``test parse empty line indicating start of message`` (t: TestContext) = 
    let found = Tokenizer.parseHeader ""
    if found <> Tokenizer.EmptyHeader then Fail(found)

let binaryReader (sample: string): BinaryReader = 
    let bytes = Encoding.UTF8.GetBytes(sample)
    let stream = new MemoryStream(bytes)
    new BinaryReader(stream, Encoding.UTF8)

let ``test take header token`` (t: TestContext) = 
    let sample = "Line 1\r\n\
                    Line 2"
    let found = Tokenizer.readLine (binaryReader sample)
    if found <> (Some "Line 1") then Fail(found)

let ``test allow newline without carriage-return`` (t: TestContext) = 
    let sample = "Line 1\n\
                    Line 2"
    let found = Tokenizer.readLine (binaryReader sample)
    if found <> (Some "Line 1") then Fail(found)

let ``test take message token`` (t: TestContext) = 
    let sample = "{}\r\n\
                    next line..."
    let found = Tokenizer.readLength 2 (binaryReader sample)
    if found <> "{}" then Fail(found)

let ``test tokenize stream`` (t: TestContext) = 
    let sample = "Content-Length: 2\r\n\
                    \r\n\
                    {}\
                    Content-Length: 1\r\n\
                    \r\n\
                    1"
    let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
    if found <> ["{}"; "1"] then Fail(found)

let ``test tokenize stream with multibyte characters`` (t: TestContext) = 
    let sample = "Content-Length: 4\r\n\
                    \r\n\
                    🔥\
                    Content-Length: 4\r\n\
                    \r\n\
                    🐼"
    let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
    if found <> ["🔥"; "🐼"] then Fail(found)
