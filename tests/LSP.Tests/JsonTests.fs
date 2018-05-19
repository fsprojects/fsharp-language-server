module LSP.JsonTests

open System
open System.Text.RegularExpressions
open LSP.Json
open LSP.Json.Ser
open NUnit.Framework

[<SetUp>]
let setup() = 
    LSP.Log.diagnosticsLog := stdout

let removeSpace(expected: string) = 
    Regex.Replace(expected, @"\s", "")

[<Test>]
let ``remove space from string`` () = 
    let found = removeSpace("foo bar")
    Assert.AreEqual("foobar", found)

[<Test>]
let ``remove newline from string`` () = 
    let actual = """foo 
    bar"""
    let found = removeSpace(actual)
    Assert.AreEqual("foobar", found)

[<Test>]
let ``serialize primitive types to JSON`` () = 
    let found = serializerFactory<bool> defaultJsonWriteOptions true
    Assert.AreEqual("true", found)
    let found = serializerFactory<int> defaultJsonWriteOptions 1
    Assert.AreEqual("1", found)
    let found = serializerFactory<string> defaultJsonWriteOptions "foo"
    Assert.AreEqual("\"foo\"", found)
    let found = serializerFactory<char> defaultJsonWriteOptions 'f'
    Assert.AreEqual("\"f\"", found)

[<Test>]
let ``serialize URI to JSON`` () = 
    let example = Uri("https://google.com")
    let found = serializerFactory<Uri> defaultJsonWriteOptions example
    Assert.AreEqual("\"https://google.com/\"", found)

[<Test>]
let ``serialize JsonValue to JSON`` () = 
    let example = JsonValue.Parse "{}"
    let found = serializerFactory<JsonValue> defaultJsonWriteOptions example
    Assert.AreEqual("{}", found)

[<Test>]
let ``serialize option to JSON`` () = 
    let found = serializerFactory<int option> defaultJsonWriteOptions (Some 1)
    Assert.AreEqual("1", found)
    let found = serializerFactory<int option> defaultJsonWriteOptions (None)
    Assert.AreEqual("null", found)

type SimpleRecord = {simpleMember: int}

[<Test>]
let ``serialize record to JSON`` () = 
    let record = {simpleMember = 1}
    let found = serializerFactory<SimpleRecord> defaultJsonWriteOptions record
    Assert.AreEqual("""{"simpleMember":1}""", found)

[<Test>]
let ``serialize list of ints to JSON`` () = 
    let example = [1; 2]
    let found = serializerFactory<int list> defaultJsonWriteOptions example
    Assert.AreEqual("""[1,2]""", found)

[<Test>]
let ``serialize list of strings to JSON`` () = 
    let example = ["foo"; "bar"]
    let found = serializerFactory<string list> defaultJsonWriteOptions example
    Assert.AreEqual("""["foo","bar"]""", found)

[<Test>]
let ``serialize a record with a custom writer`` () = 
    let record = {simpleMember = 1}
    let customWriter(r: SimpleRecord): string = sprintf "simpleMember=%d" r.simpleMember
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let found = serializerFactory<SimpleRecord> options record
    Assert.AreEqual("\"simpleMember=1\"", found)

type Foo = Bar | Doh 
type FooRecord = {foo: Foo}

[<Test>]
let ``serialize a union with a custom writer`` () = 
    let record = {foo = Bar}
    let customWriter = function 
    | Bar -> 10
    | Doh -> 20
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let found = serializerFactory<FooRecord> options record
    Assert.AreEqual("""{"foo":10}""", found)

// type UnionWithFields =
// | OptionA of A: string 
// | OptionB of int 

// [<Test>]
// let ``serialize union with fields`` () = 
//     let options = defaultJsonReadOptions
//     let serializer = serializerFactory<UnionWithFields>
//     let found = serializer options (OptionA "foo")
//     Assert.AreEqual("""{"A":"foo"}""", found)
//     let serializer = serializerFactory<UnionWithFields>
//     let found = serializer options (OptionB 1)
//     Assert.AreEqual("""{"A":[1]}""", found)

type IFoo =
    abstract member Foo: unit -> string 
type MyFoo() = 
    interface IFoo with 
        member this.Foo() = "foo"

[<Test>]
let ``serialize an interface with a custom writer`` () = 
    let customWriter(foo: IFoo): string = 
        foo.Foo()
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let example = MyFoo()
    let found = serializerFactory<IFoo> options example
    Assert.AreEqual("\"foo\"", found)
    let found = serializerFactory<MyFoo> options example
    Assert.AreEqual("\"foo\"", found)

[<Test>]
let ``deserialize simple types`` () = 
    let options = defaultJsonReadOptions
    let found = deserializerFactory<bool> options (JsonValue.Parse "true")
    Assert.AreEqual(true, found)
    let found = deserializerFactory<int> options (JsonValue.Parse "1")
    Assert.AreEqual(1, found)
    let found = deserializerFactory<char> options (JsonValue.Parse "\"x\"")
    Assert.AreEqual('x', found)
    let found = deserializerFactory<string> options (JsonValue.Parse "\"foo\"")
    Assert.AreEqual("foo", found)
    let found = deserializerFactory<Uri> options (JsonValue.Parse "\"https://github.com\"")
    Assert.AreEqual(Uri("https://github.com"), found)
    let found = deserializerFactory<Uri> options (JsonValue.Parse "\"file:///d%3A/foo.txt\"")
    Assert.AreEqual(Uri("file:///d:/foo.txt"), found)

type TestSimpleRead = {
    oneField: int
}

[<Test>]
let ``deserialize complex types`` () = 
    let options = defaultJsonReadOptions
    let found = deserializerFactory<TestSimpleRead> options (JsonValue.Parse """{"oneField":1}""")
    Assert.AreEqual({oneField=1}, found)
    let found = deserializerFactory<int list> options (JsonValue.Parse """[1]""")
    Assert.AreEqual([1], found)
    let found = deserializerFactory<int option> options (JsonValue.Parse """1""")
    Assert.AreEqual(Some 1, found)
    let found = deserializerFactory<int option> options (JsonValue.Parse """null""")
    Assert.AreEqual(None, found)

type TestOptionalRead = {
    optionField: int option
}

[<Test>]
let ``deserialize optional types`` () = 
    let options = defaultJsonReadOptions
    let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":1}""")
    Assert.AreEqual({optionField=Some 1}, found)
    let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":null}""")
    Assert.AreEqual({optionField=None}, found)
    let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{}""")
    Assert.AreEqual({optionField=None}, found)
    let found = deserializerFactory<int option list> options (JsonValue.Parse """[1]""")
    Assert.AreEqual([Some 1], found)
    let found = deserializerFactory<int option list> options (JsonValue.Parse """[null]""")
    Assert.AreEqual([None], found)

[<Test>]
let ``deserialize map`` () = 
    let options = defaultJsonReadOptions
    let found = deserializerFactory<Map<string, int>> options (JsonValue.Parse """{"k":1}""")
    let map = Map.add "k" 1 Map.empty
    Assert.AreEqual(map, found)

type TestEnum = One | Two

let deserializeTestEnum(i: int) = 
    match i with 
    | 1 -> One  
    | 2 -> Two

[<Test>]
let ``deserialize enum`` () = 
    let options = { defaultJsonReadOptions with customReaders = [deserializeTestEnum]}
    let found = deserializerFactory<TestEnum> options (JsonValue.Parse """1""")
    Assert.AreEqual(One, found)