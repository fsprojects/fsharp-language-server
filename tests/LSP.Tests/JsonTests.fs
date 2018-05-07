module LSP.JsonTests

open System
open System.Text.RegularExpressions
open FSharp.Data
open Json
open SimpleTest

let removeSpace (expected: string) = 
    Regex.Replace(expected, @"\s", "")

let ``test remove space from string`` (t: TestContext) = 
    let found = removeSpace "foo bar"
    if found <> "foobar" then Fail(found)

let ``test remove newline from string`` (t: TestContext) = 
    let actual = """foo 
    bar"""
    let found = removeSpace actual
    if found <> "foobar" then Fail(found)

let ``test serialize primitive types to JSON`` (t: TestContext) = 
    let found = serializerFactory<bool> defaultJsonWriteOptions true
    if found <> "true" then Fail(found)
    let found = serializerFactory<int> defaultJsonWriteOptions 1
    if found <> "1" then Fail(found)
    let found = serializerFactory<string> defaultJsonWriteOptions "foo"
    if found <> "\"foo\"" then Fail(found)
    let found = serializerFactory<char> defaultJsonWriteOptions 'f'
    if found <> "\"f\"" then Fail(found)

let ``test serialize URI to JSON`` (t: TestContext) = 
    let example = Uri("https://google.com")
    let found = serializerFactory<Uri> defaultJsonWriteOptions example
    if found <> "\"https://google.com/\"" then Fail(found)

let ``test serialize JsonValue to JSON`` (t: TestContext) = 
    let example = JsonValue.Parse "{}"
    let found = serializerFactory<JsonValue> defaultJsonWriteOptions example
    if found <> "{}" then Fail(found)

let ``test serialize option to JSON`` (t: TestContext) = 
    let found = serializerFactory<int option> defaultJsonWriteOptions (Some 1)
    if found <> "1" then Fail(found)
    let found = serializerFactory<int option> defaultJsonWriteOptions (None)
    if found <> "null" then Fail(found)

type SimpleRecord = {simpleMember: int}

let ``test serialize record to JSON`` (t: TestContext) = 
    let record = {simpleMember = 1}
    let found = serializerFactory<SimpleRecord> defaultJsonWriteOptions record
    if found <> """{"simpleMember":1}""" then Fail(found)

let ``test serialize list of ints to JSON`` (t: TestContext) = 
    let example = [1; 2]
    let found = serializerFactory<int list> defaultJsonWriteOptions example
    if found <> """[1,2]""" then Fail(found)

let ``test serialize list of strings to JSON`` (t: TestContext) = 
    let example = ["foo"; "bar"]
    let found = serializerFactory<string list> defaultJsonWriteOptions example
    if found <> """["foo","bar"]""" then Fail(found)

let ``test serialize a record with a custom writer`` (t: TestContext) = 
    let record = {simpleMember = 1}
    let customWriter (r: SimpleRecord): string = sprintf "simpleMember=%d" r.simpleMember
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let found = serializerFactory<SimpleRecord> options record
    if found <> "\"simpleMember=1\"" then Fail(found)

type Foo = Bar | Doh 
type FooRecord = {foo: Foo}

let ``test serialize a union with a custom writer`` (t: TestContext) = 
    let record = {foo = Bar}
    let customWriter = function 
    | Bar -> 10
    | Doh -> 20
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let found = serializerFactory<FooRecord> options record
    if found <> """{"foo":10}""" then Fail(found)

type IFoo =
    abstract member Foo: unit -> string 
type MyFoo() = 
    interface IFoo with 
        member this.Foo() = "foo"

let ``test serialize an interface with a custom writer`` (t: TestContext) = 
    let customWriter (foo: IFoo): string = 
        foo.Foo()
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let example = MyFoo()
    let found = serializerFactory<IFoo> options example
    if found <> "\"foo\"" then Fail(found)
    let found = serializerFactory<MyFoo> options example
    if found <> "\"foo\"" then Fail(found)