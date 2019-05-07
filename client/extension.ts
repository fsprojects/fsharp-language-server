/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import { workspace, ExtensionContext, commands, StatusBarItem } from 'coc.nvim';
import { TerminalResult } from 'coc.nvim/lib/types';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from 'coc.nvim';
import { NotificationType } from 'coc.nvim/node_modules/vscode-languageserver-protocol';
import { Range } from 'coc.nvim/node_modules/vscode-languageserver-types';

export function activate(context: ExtensionContext) {

	// The server is packaged as a standalone command
	let serverMain = context.asAbsolutePath(binName());
	
	let serverOptions: ServerOptions = { 
		command: serverMain, 
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
			// TODO: is there a way to configure this via the language server protocol?
			fileEvents: [
				workspace.createFileSystemWatcher('**/*.fsproj'),
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
            this.title =  `${start.title} (${start.nFiles})`
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
                this.statusBarItem.text = `${this.title} ${newPercent}%... [${fileName}]`
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
    client.onNotification(new NotificationType('fsharp/startProgress'), (start: StartProgress) => {
        progressListener.startProgress(start);
    });
    client.onNotification(new NotificationType('fsharp/incrementProgress'), (fileName: string) => {
        progressListener.incrementProgress(fileName);
    });
    client.onNotification(new NotificationType('fsharp/endProgress'), () => {
        progressListener.endProgress();
    });
}

function binName(): string {
	var baseParts = ['bin'];
	var pathParts = getPathParts(process.platform);
	var fullParts = baseParts.concat(pathParts);

	return path.join(...fullParts);
}

function getPathParts(platform: string): string[] {
	switch (platform) {
		case 'win32':
			return ['win10-x64', 'FSharpLanguageServer.exe'];

		case 'linux':
			return ['linux-x64', 'FSharpLanguageServer'];

		case 'darwin':
			return ['osx.10.11-x64', 'FSharpLanguageServer'];
	}

	throw `unsupported platform: ${platform}`;
}

