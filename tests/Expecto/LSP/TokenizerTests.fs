module LSP.Tests.TokenizerTests

open System.IO
open System.Text
open Expecto
open LSP
LSP.Log.diagnosticsLog := stdout


let tests  =        
    testList "Parser test" [

    test "parse content length header"{
        let sample = "Content-Length: 10"
        let found = Tokenizer.parseHeader sample
        Expect.equal (Tokenizer.ContentLength 10) found "not equal"
    }
    
    test "parse content type header"{
        let sample = "Content-Type: application/vscode-jsonrpc; charset=utf-8"
        let found = Tokenizer.parseHeader sample
        Expect.equal Tokenizer.OtherHeader found "not equal"
    }
    
    test "parse empty line indicating start of message"{
        let found = Tokenizer.parseHeader ""
        Expect.equal Tokenizer.EmptyHeader found "not equal"
    }
    let binaryReader(sample: string): BinaryReader = 
        let bytes = Encoding.UTF8.GetBytes(sample)
        let stream = new MemoryStream(bytes)
        new BinaryReader(stream, Encoding.UTF8)

    
    test "take header token"{
        let sample = "Line 1\r\n\
                        Line 2"
        let found = Tokenizer.readLine (binaryReader sample)
        Expect.equal (Some "Line 1") found "not equal"
    }
    
    test "allow newline without carriage-return"{
        let sample = "Line 1\n\
                        Line 2"
        let found = Tokenizer.readLine (binaryReader sample)
        Expect.equal (Some "Line 1") found "not equal"
    }
    
    test "take message token"{
        let sample = "{}\r\n\
                        next line..."
        let found = Tokenizer.readLength(2, binaryReader(sample))
        Expect.equal "{}" found "not equal"
    }
    
    test "tokenize stream"{
        let sample = "Content-Length: 2\r\n\
                        \r\n\
                        {}\
                        Content-Length: 1\r\n\
                        \r\n\
                        1"
        let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
        Expect.equal ["{}"; "1"] found "not equal"
    }
    
    test "tokenize stream with multibyte characters"{
        let sample = "Content-Length: 5\r\n\
                        \r\n\
                        _🔥\
                        Content-Length: 5\r\n\
                        \r\n\
                        _🐼"
        let found = Tokenizer.tokenize (binaryReader sample) |> Seq.toList
        Expect.equal ["_🔥"; "_🐼"] found "not equal"
    }
    
    ]