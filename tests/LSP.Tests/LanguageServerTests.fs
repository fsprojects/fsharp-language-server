module LSP.LanguageServerTests

open System.IO 
open System.Text
open NUnit.Framework

let binaryWriter () = 
    let stream = new MemoryStream()
    let writer = new BinaryWriter(stream)
    let toString () = 
        let bytes = stream.ToArray() 
        Encoding.UTF8.GetString bytes
    (writer, toString)

[<Test>]
let ``write text``() = 
    let (writer, toString) = binaryWriter() 
    writer.Write (Encoding.UTF8.GetBytes "foo")
    Assert.That(toString(), Is.EqualTo "foo")

[<Test>]
let ``write response``() = 
    let (writer, toString) = binaryWriter() 
    LanguageServer.respond writer 1 "2"
    let expected = "Content-Length: 19\r\n\r\n\
                    {\"id\":1,\"result\":2}"
    Assert.That(toString(), Is.EqualTo expected)

[<Test>]
let ``write multibyte characters``() = 
    let (writer, toString) = binaryWriter() 
    LanguageServer.respond writer 1 "🔥"
    let expected = "Content-Length: 22\r\n\r\n\
                    {\"id\":1,\"result\":🔥}"
    Assert.That(toString(), Is.EqualTo expected)