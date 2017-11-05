module LSP.JsonTests

open Types
open Parser
open Json
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
    Assert.That(serializerFactory<bool> defaultJsonWriteOptions true, Is.EqualTo("true"))
    Assert.That(serializerFactory<int> defaultJsonWriteOptions 1, Is.EqualTo("1"))
    Assert.That(serializerFactory<string> defaultJsonWriteOptions "foo", Is.EqualTo("\"foo\""))

[<Test>]
let ``serialize option to JSON`` () = 
    Assert.That(serializerFactory<option<int>> defaultJsonWriteOptions (Some 1), Is.EqualTo("1"))
    Assert.That(serializerFactory<option<int>> defaultJsonWriteOptions (None), Is.EqualTo("null"))

type SimpleRecord = {simpleMember: int}

[<Test>]
let ``serialize record to JSON`` () = 
    let record = {simpleMember = 1}
    Assert.That(serializerFactory<SimpleRecord> defaultJsonWriteOptions record, Is.EqualTo("""{"simpleMember":1}"""))

[<Test>]
let ``serialize list of ints to JSON`` () = 
    let example = [1; 2]
    Assert.That(serializerFactory<list<int>> defaultJsonWriteOptions example, Is.EqualTo("""[1,2]"""))

[<Test>]
let ``serialize list of strings to JSON`` () = 
    let example = ["foo"; "bar"]
    Assert.That(serializerFactory<list<string>> defaultJsonWriteOptions example, Is.EqualTo("""["foo","bar"]"""))

[<Test>]
let ``serialize a record with a custom writer`` () = 
    let record = {simpleMember = 1}
    let customWriter (r: SimpleRecord): string = sprintf "simpleMember=%d" r.simpleMember
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    Assert.That(serializerFactory<SimpleRecord> options record, Is.EqualTo("\"simpleMember=1\""))

type Foo = Bar | Doh 
type FooRecord = {foo: Foo}

[<Test>]
let ``serialize a union with a custom writer`` () = 
    let record = {foo = Bar}
    let customWriter = function 
    | Bar -> 10
    | Doh -> 20
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    Assert.That(serializerFactory<FooRecord> options record, Is.EqualTo("""{"foo":10}"""))

type IFoo =
    abstract member Foo: unit -> string 
type MyFoo() = 
    interface IFoo with 
        member this.Foo() = "foo"

[<Test>]
let ``serialize an interface with a custom writer`` () = 
    let customWriter (foo: IFoo): string = 
        foo.Foo()
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let example = MyFoo()
    Assert.That(serializerFactory<IFoo> options example, Is.EqualTo("\"foo\""))
    Assert.That(serializerFactory<MyFoo> options example, Is.EqualTo("\"foo\""))