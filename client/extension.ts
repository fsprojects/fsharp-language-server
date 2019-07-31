/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path'
import * as fs from 'fs'
import { FsiProcess } from './process'
import { workspace, ExtensionContext, commands, StatusBarItem, TerminalResult } from 'coc.nvim';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from 'coc.nvim';
import { NotificationType } from 'vscode-jsonrpc';
import { Range } from 'vscode-languageserver-protocol';
import {OperatingSystem, currentPlatform, languageServerExe, downloadLanguageServer} from './platform'


async function getCurrentSelection(mode: string) {
    let doc = await workspace.document

    if (mode === "v" || mode === "V") {
        let [from, _ ] = await doc.buffer.mark("<")
        let [to, __  ] = await doc.buffer.mark(">")
        let result: string[] = []
        for(let i = from; i <= to; ++i)
        {
            result.push(doc.getline(i - 1))
        }
        return result
    }
    else if (mode === "n") {
        let line = await workspace.nvim.call('line', '.')
        return [doc.getline(line - 1)]
    }
    else if (mode === "i") {
        // TODO what to do in insert mode?
    }
    else if (mode === "t") {
        //TODO what to do in terminal mode?
    }

    return []
}

let currentREPL: FsiProcess = undefined
async function createREPL () {
    if(currentREPL) {
        currentREPL.dispose()
        currentREPL = undefined
    }
    currentREPL = new FsiProcess("F# REPL") 
    currentREPL.onExited(() => {
        currentREPL = undefined
    })
    await currentREPL.start()
    return currentREPL.onExited
}


async function doEval(mode: string) {

    let document = await workspace.document
    if (!document || document.filetype !== 'fsharp') {
        return
    }

    if(!currentREPL) {
        await createREPL()
    }

    // TODO: move to workspace.getCurrentSelection when we get an answer:
    // https://github.com/neoclide/coc.nvim/issues/933
    const content = await getCurrentSelection(mode)
    for(let line of content){
        await currentREPL.eval(line)
    }
    await currentREPL.eval(";;")
    // see :help feedkeys
    await workspace.nvim.call('eval', `feedkeys("\\<esc>${content.length}j", "in")`)
    // await currentREPL.scrollToBottom()
}


function registerREPL(context: ExtensionContext, __: string) { 

    let cmdEvalLine = commands.registerCommand("fsharp.evaluateLine", async () => doEval('n'));
    let cmdEvalSelection = commands.registerCommand("fsharp.evaluateSelection", async () => doEval('v'));
    let cmdExecFile = commands.registerCommand("fsharp.run", async (...args: any[]) => {
        let root = workspace.rootPath

        let argStrs = args
            ? args.map(x => `${x}`)
            : []

        if (currentREPL) {
            currentREPL.log.appendLine(`executing F# project...`)
        }

        let term = await workspace.createTerminal({
            name: `F# console`,
            shellPath: "dotnet",
            cwd: root,
            shellArgs: ['run'].concat(argStrs)
        })

        // switch to the terminal and steal focus
        term.show(false)
    })

    // Push the disposable to the context's subscriptions so that the 
    // client can be deactivated on extension deactivation
    context.subscriptions.push(cmdExecFile, cmdEvalLine, cmdEvalSelection);
    return createREPL
}


export async function activate(context: ExtensionContext) {

    // The server is packaged as a standalone command

    if (!fs.existsSync(languageServerExe)) {
        let item = workspace.createStatusBarItem(0, {progress: true})
        item.text = "Downloading F# Language Server"
        item.show()
        await downloadLanguageServer()
        item.dispose()
    }

    // Make sure the server is executable
    if (currentPlatform.operatingSystem !== OperatingSystem.Windows) {
        fs.chmodSync(languageServerExe, "755")
    }

    let serverOptions: ServerOptions = { 
        command: languageServerExe, 
        args: [], 
        transport: TransportKind.stdio
    }

    // Options to control the language client
    let clientOptions: LanguageClientOptions = {
        // Register the server for F# documents
        documentSelector: [{scheme: 'file', language: 'fsharp'}],
        synchronize: {
            // Synchronize the setting section 'fsharp' to the server
            configurationSection: 'fsharp',
            // Notify the server about file changes to F# project files contain in the workspace
            fileEvents: [
                workspace.createFileSystemWatcher('**/*.fsproj'),
                workspace.createFileSystemWatcher('**/*.fs'),
                workspace.createFileSystemWatcher('**/*.fsi'),
                workspace.createFileSystemWatcher('**/*.fsx'),
                workspace.createFileSystemWatcher('**/project.assets.json')
            ]
        }
    }

    // Create the language client and start the client.
    let client = new LanguageClient('fsharp', 'F# Language Server', serverOptions, clientOptions);
    let disposable = client.start();

    // Push the disposable to the context's subscriptions so that the 
    // client can be deactivated on extension deactivation
    context.subscriptions.push(disposable);

    // When the language client activates, register a progress-listener
    client.onReady().then(() => createProgressListeners(client));

    // Register test-runner
    commands.registerCommand('fsharp.command.test.run', runTest);
    commands.registerCommand('fsharp.command.goto', goto);

    registerREPL(context, "F# REPL")
}

function goto(file: string, startLine: number, startColumn: number, _endLine: number, _endColumn: number) {
    let selection = Range.create(startLine, startColumn, startLine, startColumn);
    workspace.jumpTo(file, selection.start);
}

function runTest(projectPath: string, fullyQualifiedName: string): Thenable<TerminalResult> {
    let command = `dotnet test ${projectPath} --filter FullyQualifiedName=${fullyQualifiedName}`
    return workspace.runTerminalCommand(command);

    // !TODO parse the results coming back...
    //let kind: FSharpTestTask = {
    //type: 'fsharp.task.test',
    //projectPath: projectPath,
    //fullyQualifiedName: fullyQualifiedName
    //}
    //let task = workspace.createTask(`fsharp.task.test`);
    //let shell = new ShellExecution('dotnet', args)
    //let uri = Uri.file(projectPath)
    //let workspaceFolder = workspace.getWorkspaceFolder(uri.fsPath)
    //let task = new Task(kind, workspaceFolder, 'F# Test', 'F# Language Server', shell)
    //return tasks.executeTask(task)
}

interface StartProgress {
    title: string 
    nFiles: number
}

function createProgressListeners(client: LanguageClient) {
    // Create a "checking files" progress indicator
    let progressListener = new class {
        countChecked = 0
        nFiles = 0
        title: string = ""
        statusBarItem: StatusBarItem = null;

        startProgress(start: StartProgress) {
            // TODO implement user cancellation (???)
            this.title =  start.title
            this.nFiles = start.nFiles
            this.statusBarItem = workspace.createStatusBarItem(0, { progress : true });
            this.statusBarItem.text = this.title;
        }

        private percentComplete() {
            return Math.floor(this.countChecked / (this.nFiles + 1) * 100);
        }

        incrementProgress(fileName: string) {
            if (this.statusBarItem != null) {
                this.countChecked++;
                let newPercent = this.percentComplete();
                this.statusBarItem.text = `${this.title} (${newPercent}%)... [${fileName}]`
                this.statusBarItem.show();
            }
        }

        endProgress() {
            this.countChecked = 0
            this.nFiles = 0
            this.statusBarItem.hide()
            this.statusBarItem.dispose()
            this.statusBarItem = null
        }
    }

    // Use custom notifications to drive progressListener
    client.onNotification('fsharp/startProgress', (start: StartProgress) => {
        progressListener.startProgress(start);
    });
    client.onNotification('fsharp/incrementProgress', (fileName: string) => {
        progressListener.incrementProgress(fileName);
    });
    client.onNotification('fsharp/endProgress', () => {
        progressListener.endProgress();
    });
}

