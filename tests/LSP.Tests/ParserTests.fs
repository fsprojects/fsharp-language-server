namespace LSP

open System
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
    let ``parse a Cancel notification`` () = 
        let json = JsonValue.Parse """{
            "id": 1
        }""" 
        Assert.That(
            parseNotification "cancel" (Some json), 
            Is.EqualTo (Cancel 1))

    let ``parse an Initialized notification`` () = 
        Assert.That(
            parseNotification "initialized" None,
            Is.EqualTo (Initialized))

    [<Test>]
    let ``parse a minimal Initialize request`` () = 
        let json = JsonValue.Parse """{
            "processId": 1,
            "rootUri": "file://workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest "initialize" json
        Assert.That(
            i, 
            Is.EqualTo({
                processId = Some 1;
                rootUri = Some (Uri("file://workspace"));
                initializationOptions = None;
                capabilitiesMap = Map.empty;
                trace = None}))
    
    [<Test>]
    let ``processId can be null`` () = 
        let json = JsonValue.Parse """{
            "processId": null,
            "rootUri": "file://workspace",
            "capabilities": {
            }
        }"""
        let (Initialize i) = parseRequest "initialize" json 
        Assert.That(i.processId, Is.EqualTo(None))

    [<Test>]
    let ``parse capabilities as map`` () = 
        let json = JsonValue.Parse """{
            "processId": 1,
            "rootUri": "file://workspace",
            "capabilities": {
                "workspace": {
                    "workspaceEdit": {
                        "documentChanges": true
                    }
                }
            }
        }"""
        let (Initialize i) = parseRequest "initialize" json 
        Assert.That(i.capabilitiesMap, Is.EquivalentTo(Map.empty.Add("workspace.workspaceEdit.documentChanges", true)))