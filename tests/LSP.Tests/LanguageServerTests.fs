module LSP.LanguageServerTests

open Types
open Parser
open LanguageServer
open System.Runtime.Serialization
open NUnit.Framework
open System.Text.RegularExpressions

[<DataContract>]
type Simple = {
    [<field: DataMember(Name="simpleProp")>]
    simpleProp: int
}

[<Test>]
let ``serialize a simple record class to JSON`` () = 
    let serializer = serializerFactory<Simple>()
    Assert.That(serializer({simpleProp = 1}), Is.EqualTo("""{"simpleProp":1}"""))

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