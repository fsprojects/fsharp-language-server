/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/

import { workspace, extensions, ExtensionContext } from 'coc.nvim'
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

export function getPlatformSignature(): string
{
    const plat = getPlatformDetails()

    const os_sig = (()=>{
        switch(plat.operatingSystem){
            case OperatingSystem.Windows: return "win"
            case OperatingSystem.Linux: return "linux"
            case OperatingSystem.MacOS: return "osx"
            default: return "unknown"
        }
    })()

    const arch_sig = (()=>{
        if(plat.isProcess64Bit) return "x64"
        else return "x86"
    })()

    return `${os_sig}-${arch_sig}`
}

export interface ILanguageServerPackage
{
    executable: string
    downloadUrl: string
}

export interface ILanguageServerRepository
{
    [platform:string]: ILanguageServerPackage 
}

export type LanguageServerDownloadChannel =
    | { type: "nightly" }
    | { type: "latest" }
    | { type: "specific-tag" }

export class LanguageServerProvider
{
    private extensionStoragePath: string
    private languageServerDirectory: string
    private languageServerZip: string
    private languageServerExe: string
    private languageServerPackage: ILanguageServerPackage

    constructor(private extension: ExtensionContext, private repo: ILanguageServerRepository, private channel: LanguageServerDownloadChannel)
    {
        const platsig = getPlatformSignature()
        this.extensionStoragePath = extension.storagePath
        this.languageServerPackage = repo[platsig]

        if(!this.languageServerPackage) { throw "Platform not supported" }

        this.languageServerDirectory = path.join(this.extensionStoragePath, "server")
        this.languageServerZip = this.languageServerDirectory + ".zip"
        this.languageServerExe = path.join(this.languageServerDirectory, this.languageServerPackage.executable)
    }

    public async downloadLanguageServer(): Promise<void> {

        let item = workspace.createStatusBarItem(0, {progress: true})
        item.text = "Downloading F# Language Server"
        item.show()

        if(!fs.existsSync(this.extensionStoragePath)) {
            fs.mkdirSync(this.extensionStoragePath)
        }

        if(fs.existsSync(this.languageServerDirectory)){
            rimraf.sync(this.languageServerDirectory)
        }

        let url = this.languageServerPackage.downloadUrl

        if(this.channel.type === "nightly")
        {
            url = url.replace("RELEASE", "nightly")
        }

        fs.mkdirSync(this.languageServerDirectory)

        await new Promise<void>((resolve, reject) => {
            const req = followRedirects.https.request(url, (res: IncomingMessage) => {
              if (res.statusCode != 200) {
                reject(new Error(`Invalid response from ${url}: ${res.statusCode}`))
                return
              }
              let file = fs.createWriteStream(this.languageServerZip)
              let stream = res.pipe(file)
              stream.on('finish', resolve)
            })
            req.on('error', reject)
            req.end()
        })

        await new Promise<void>((resolve, reject) => {
            unzip(this.languageServerZip, {dir: this.languageServerDirectory}, (err: any) => {
                if(err) reject(err)
                else resolve()
            })
        })

        fs.unlinkSync(this.languageServerZip)
        item.dispose()
    }

    // returns the full path to the language server executable
    public async getLanguageServer(): Promise<string> {

        const plat = getPlatformDetails()

        if (!fs.existsSync(this.languageServerExe)) {
            await this.downloadLanguageServer()
        }

        // Make sure the server is executable
        if (plat.operatingSystem !== OperatingSystem.Windows) {
            fs.chmodSync(this.languageServerExe, "755")
        }

        return this.languageServerExe
    }
}

