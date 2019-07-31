/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/

import { workspace } from 'coc.nvim'
import {sleep} from "./utils"
import fs = require("fs");
import path = require("path");
import process = require("process");
import { IncomingMessage, RequestOptions, Agent } from 'http'
import { parse } from 'url'
const tunnel = require('tunnel')
const followRedirects = require("follow-redirects")
const unzip = require("extract-zip");
const rimraf = require("rimraf")

export enum OperatingSystem {
    Unknown,
    Windows,
    MacOS,
    Linux,
}

export interface IPlatformDetails {
    operatingSystem: OperatingSystem;
    isOS64Bit: boolean;
    isProcess64Bit: boolean;
}

export function getPlatformDetails(): IPlatformDetails {
    let operatingSystem = OperatingSystem.Unknown;

    if (process.platform === "win32") {
        operatingSystem = OperatingSystem.Windows;
    } else if (process.platform === "darwin") {
        operatingSystem = OperatingSystem.MacOS;
    } else if (process.platform === "linux") {
        operatingSystem = OperatingSystem.Linux;
    }

    const isProcess64Bit = process.arch === "x64";

    return {
        operatingSystem,
        isOS64Bit: isProcess64Bit || process.env.hasOwnProperty("PROCESSOR_ARCHITEW6432"),
        isProcess64Bit,
    };
}

export const currentPlatform = getPlatformDetails()
export const languageServerDirectory = path.join(__dirname, "..", "server")
const languageServerZip  = languageServerDirectory + ".zip"
const URL_Windows        = "https://github.com/yatli/coc-fsharp/releases/download/RELEASE/coc-fsharp-win10-x64.zip"
const URL_Osx            = "https://github.com/yatli/coc-fsharp/releases/download/RELEASE/coc-fsharp-osx.10.11-x64.zip"
const URL_Linux          = "https://github.com/yatli/coc-fsharp/releases/download/RELEASE/coc-fsharp-linux-x64.zip"

export const languageServerExe = (() => {
    if (currentPlatform.operatingSystem === OperatingSystem.Windows)
        return path.join(languageServerDirectory, "FSharpLanguageServer.exe")
    else
        return path.join(languageServerDirectory, "FSharpLanguageServer")
})()

export async function downloadLanguageServer() {

    if(fs.existsSync(languageServerDirectory)){
        rimraf.sync(languageServerDirectory)
    }

    let url = (()=>{
        switch(currentPlatform.operatingSystem) {
            case OperatingSystem.Windows: return URL_Windows 
            case OperatingSystem.Linux: return URL_Linux
            case OperatingSystem.MacOS: return URL_Osx
            default: throw "Unsupported operating system"
        }
    })().replace("RELEASE", "nightly")

    fs.mkdirSync(languageServerDirectory)

    await new Promise<void>((resolve, reject) => {
        const req = followRedirects.https.request(url, (res: IncomingMessage) => {
          if (res.statusCode != 200) {
            reject(new Error(`Invalid response from ${url}: ${res.statusCode}`))
            return
          }
          let file = fs.createWriteStream(languageServerZip)
          let stream = res.pipe(file)
          stream.on('finish', resolve)
        })
        req.on('error', reject)
        req.end()
    })

    await new Promise<void>((resolve, reject) => {
        unzip(languageServerZip, {dir: languageServerDirectory}, (err: any) => {
            if(err) reject(err)
            else resolve()
        })
    })

    fs.unlinkSync(languageServerZip)
}
