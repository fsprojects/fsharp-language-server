namespace LSP

open Parser
open NUnit.Framework
open FSharp.Data

module ParserTests =
    [<Test>]
    let ``parse a RequestMessage`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld"
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (RequestMessage (1, "helloWorld", None)))

    [<Test>]
    let ``parse a RequestMessage with params`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld",
            "params": {"hello": "world"}
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (RequestMessage (1, "helloWorld", Some (JsonValue.Parse """{"hello":"world"}"""))))

    [<Test>]
    let ``parse a NotificationMessage`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification"
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (NotificationMessage ("helloNotification", None)))

    [<Test>]
    let ``parse a NotificationMessage with params`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "method": "helloNotification",
            "params": {"hello": "world"}
        }"""
        Assert.That(
            parseMessage json, 
            Is.EqualTo (NotificationMessage ("helloNotification", Some (JsonValue.Parse """{"hello":"world"}"""))))

    [<Test>]
    let ``parse a Cancel message`` () = 
        let json = JsonValue.Parse """{
            "id": 1
        }""" 
        Assert.That(parseNotification json, Is.EqualTo (Cancel 1))