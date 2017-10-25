module LSP.LanguageServerTests

open Types
open Parser
open LanguageServer
open System.Runtime.Serialization
open NUnit.Framework

[<DataContract>]
type Simple = {
    [<field: DataMember(Name="simpleProp")>]
    simpleProp: int
}

[<Test>]
let ``serialize a simple record class to JSON`` () = 
    let serializer = serializerFactory<Simple>()
    Assert.That(serializer({simpleProp = 1}), Is.EqualTo("""{"simpleProp":1}"""))