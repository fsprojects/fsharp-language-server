module LSP.JsonTests

open System
open System.Text.RegularExpressions
open FSharp.Data
open Json
open Xunit

let removeSpace (expected: string) = 
    Regex.Replace(expected, @"\s", "")

[<Fact>]
let ``remove space from string`` () = 
    Assert.Equal(removeSpace "foo bar", "foobar")

[<Fact>]
let ``remove newline from string`` () = 
    let actual = """foo 
    bar"""
    Assert.Equal(removeSpace actual, "foobar")

[<Fact>]
let ``serialize primitive types to JSON`` () = 
    Assert.Equal(serializerFactory<bool> defaultJsonWriteOptions true, ("true"))
    Assert.Equal(serializerFactory<int> defaultJsonWriteOptions 1, ("1"))
    Assert.Equal(serializerFactory<string> defaultJsonWriteOptions "foo", ("\"foo\""))
    Assert.Equal(serializerFactory<char> defaultJsonWriteOptions 'f', ("\"f\""))

[<Fact>]
let ``serialize URI to JSON`` () = 
    let example = Uri("https://google.com")
    Assert.Equal(serializerFactory<Uri> defaultJsonWriteOptions example, ("\"https://google.com/\""))

[<Fact>]
let ``serialize JsonValue to JSON`` () = 
    let example = JsonValue.Parse "{}"
    Assert.Equal(serializerFactory<JsonValue> defaultJsonWriteOptions example, ("{}"))

[<Fact>]
let ``serialize option to JSON`` () = 
    Assert.Equal(serializerFactory<option<int>> defaultJsonWriteOptions (Some 1), ("1"))
    Assert.Equal(serializerFactory<option<int>> defaultJsonWriteOptions (None), ("null"))

type SimpleRecord = {simpleMember: int}

[<Fact>]
let ``serialize record to JSON`` () = 
    let record = {simpleMember = 1}
    Assert.Equal(serializerFactory<SimpleRecord> defaultJsonWriteOptions record, ("""{"simpleMember":1}"""))

[<Fact>]
let ``serialize list of ints to JSON`` () = 
    let example = [1; 2]
    Assert.Equal(serializerFactory<list<int>> defaultJsonWriteOptions example, ("""[1,2]"""))

[<Fact>]
let ``serialize list of strings to JSON`` () = 
    let example = ["foo"; "bar"]
    Assert.Equal(serializerFactory<list<string>> defaultJsonWriteOptions example, ("""["foo","bar"]"""))

[<Fact>]
let ``serialize a record with a custom writer`` () = 
    let record = {simpleMember = 1}
    let customWriter (r: SimpleRecord): string = sprintf "simpleMember=%d" r.simpleMember
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    Assert.Equal(serializerFactory<SimpleRecord> options record, ("\"simpleMember=1\""))

type Foo = Bar | Doh 
type FooRecord = {foo: Foo}

[<Fact>]
let ``serialize a union with a custom writer`` () = 
    let record = {foo = Bar}
    let customWriter = function 
    | Bar -> 10
    | Doh -> 20
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    Assert.Equal(serializerFactory<FooRecord> options record, ("""{"foo":10}"""))

type IFoo =
    abstract member Foo: unit -> string 
type MyFoo() = 
    interface IFoo with 
        member this.Foo() = "foo"

[<Fact>]
let ``serialize an interface with a custom writer`` () = 
    let customWriter (foo: IFoo): string = 
        foo.Foo()
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let example = MyFoo()
    Assert.Equal(serializerFactory<IFoo> options example, ("\"foo\""))
    Assert.Equal(serializerFactory<MyFoo> options example, ("\"foo\""))