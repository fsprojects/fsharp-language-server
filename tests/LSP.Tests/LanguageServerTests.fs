module LSP.LanguageServerTests

open Types
open Parser
open LanguageServer
open System.Runtime.Serialization
open NUnit.Framework
open System.Text.RegularExpressions

let removeSpace (expected: string) = 
    Regex.Replace(expected, @"\s", "")

[<Test>]
let ``remove space from string`` () = 
    Assert.That(removeSpace "foo bar", Is.EqualTo "foobar")

[<Test>]
let ``remove newline from string`` () = 
    let actual = """foo 
    bar"""
    Assert.That(removeSpace actual, Is.EqualTo "foobar")

[<Test>]
let ``serialize primitive types to JSON`` () = 
    Assert.That(serializerFactory<bool>() true, Is.EqualTo("true"))
    Assert.That(serializerFactory<int>() 1, Is.EqualTo("1"))
    Assert.That(serializerFactory<string>() "foo", Is.EqualTo("\"foo\""))

[<Test>]
let ``serialize option to JSON`` () = 
    Assert.That(serializerFactory<option<int>>() (Some 1), Is.EqualTo("1"))
    Assert.That(serializerFactory<option<int>>() (None), Is.EqualTo("null"))

type SimpleRecord = {simpleMember: int}

[<Test>]
let ``serialize record to JSON`` () = 
    Assert.That(serializerFactory<SimpleRecord>() {simpleMember = 1}, Is.EqualTo("""{"simpleMember":1}"""))