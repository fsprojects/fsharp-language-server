/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import * as fs from "fs";
import * as cp from 'child_process';
import { window, workspace, ExtensionContext, Range, commands, tasks, Task, TaskExecution, ShellExecution, Uri, TaskDefinition, debug } from 'vscode';
// import { NotificationType } from 'vscode-languageclient';
import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	TransportKind
} from 'vscode-languageclient/node';
//import { env } from 'process';
// Run using `dotnet` instead of self-contained executable
let client: LanguageClient;

export function activate(context: ExtensionContext) {
	//let FSLangServerFolder = Uri.joinPath(workspace.workspaceFolders[0].uri, ('src/FSharpLanguageServer'));
	const debugMode = workspace.getConfiguration().get("fsharp.debug.enable", false);

	const customCommand: string = workspace.getConfiguration().get("fsharp.customCommand", null);

	const customCommandArgs: string[] = workspace.getConfiguration().get("fsharp.customCommandArgs", null);
	const customDllPath: string = workspace.getConfiguration().get("fsharp.customDllPath", null);
	let customDllArgs = null

	if (customDllPath != null && customDllPath != "") customDllArgs = [customDllPath];

	let args: string[] = customCommandArgs ?? (customDllArgs ?? [binName()])
	if (debugMode) {
		args.push("--attach-debugger")
	}
	//This always needs to be just a single command with no args. If not it will cause an error.
	let serverMain = customCommand ?? findInPath('dotnet') ?? 'dotnet';

	// The server is packaged as a standalone command


	console.log("Going to start server with command  ", serverMain, args);

	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	let serverOptions: ServerOptions = {
		command: serverMain,
		args: args,
		transport: TransportKind.stdio,
		options: {
			cwd: context.extensionPath,
			env: {
				...process.env,
			}

		}
	}

	// Options to control the language client
	let clientOptions: LanguageClientOptions = {
		// Register the server for F# documents
		documentSelector: [{ scheme: 'file', language: 'fsharp' }],
		synchronize: {
			// Synchronize the setting section 'languageServerExample' to the server
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
	client = new LanguageClient('fsharp', 'F# Language Server', serverOptions, clientOptions);
	client.start();

	// Push the disposable to the context's subscriptions so that the 
	// client can be deactivated on extension deactivation

	// When the language client activates, register a progress-listener

	// Register test-runner
	commands.registerCommand('fsharp.command.test.run', runTest);
	commands.registerCommand('fsharp.command.test.debug', debugTest);
	commands.registerCommand('fsharp.command.goto', goto);
}

export function deactivate(): Thenable<void> | undefined {
	if (!client) {
		return undefined;
	}
	return client.stop();
}

function goto(file: string, startLine: number, startColumn: number, _endLine: number, _endColumn: number) {
	let selection = new Range(startLine, startColumn, startLine, startColumn);
	workspace.openTextDocument(file).then(doc => window.showTextDocument(doc, { selection }));
}

interface FSharpTestTask extends TaskDefinition {
	projectPath: string
	fullyQualifiedName: string
}

function runTest(projectPath: string, fullyQualifiedName: string): Thenable<TaskExecution> {
	let args = ['test', projectPath, '--filter', `FullyQualifiedName=${fullyQualifiedName}`]
	let kind: FSharpTestTask = {
		type: 'fsharp.task.test',
		projectPath: projectPath,
		fullyQualifiedName: fullyQualifiedName
	}
	let shell = new ShellExecution('dotnet', args)
	let workspaceFolder = workspace.getWorkspaceFolder(Uri.file(projectPath))
	let task = new Task(kind, workspaceFolder, 'F# Test', 'F# Language Server', shell)
	return tasks.executeTask(task)
}

const outputChannel = window.createOutputChannel('F# Debug Tests');

function debugTest(projectPath: string, fullyQualifiedName: string): Promise<number> {
	return new Promise((resolve, _reject) => {
		// TODO replace this with the tasks API once stdout is available
		// https://code.visualstudio.com/docs/extensionAPI/vscode-api#_tasks
		// https://github.com/Microsoft/vscode/issues/45980
		let cmd = 'dotnet'
		let args = ['test', projectPath, '--filter', `FullyQualifiedName=${fullyQualifiedName}`]
		let child = cp.spawn(cmd, args, {
			env: {
				...process.env,
				'VSTEST_HOST_DEBUG': '1'
			}
		})

		outputChannel.clear()
		outputChannel.show()
		outputChannel.appendLine(`${cmd} ${args.join(' ')}...`)

		var isWaitingForDebugger = false
		function onStdoutLine(line: string) {
			if (line.trim() == 'Waiting for debugger attach...') {
				isWaitingForDebugger = true
			}
			if (isWaitingForDebugger) {
				let pattern = /^Process Id: (\d+)/
				let match = line.match(pattern)
				if (match) {
					let pid = Number.parseInt(match[1])
					let workspaceFolder = workspace.getWorkspaceFolder(Uri.file(projectPath))
					let config = {
						"name": "F# Test",
						"type": "coreclr",
						"request": "attach",
						"processId": pid
					}
					outputChannel.appendLine(`Attaching debugger to process ${pid}...`)
					debug.startDebugging(workspaceFolder, config)

					isWaitingForDebugger = false
				}
			}
		}

		var stdoutBuffer = ''
		function onStdoutChunk(chunk: string | Buffer) {
			// Append to output channel
			let string = chunk.toString()
			outputChannel.append(string)
			// Send each line to onStdoutLine
			stdoutBuffer += string
			var newline = stdoutBuffer.indexOf('\n')
			while (newline != -1) {
				let line = stdoutBuffer.substring(0, newline)
				onStdoutLine(line)
				stdoutBuffer = stdoutBuffer.substring(newline + 1)
				newline = stdoutBuffer.indexOf('\n')
			}
		}

		child.stdout.on('data', onStdoutChunk);
		child.stderr.on('data', chunk => outputChannel.append(chunk.toString()));
		child.on('close', (code, _signal) => resolve(code))
	})
}

function binName() {
	var baseParts = ['src', 'FSharpLanguageServer', 'bin', 'Release', 'net6.0'];
	var pathParts = getPathParts();
	var fullParts = baseParts.concat(pathParts);

	return path.join(...fullParts);
}



function getPathParts(): string[] {
	/* switch (platform) {
		case 'win32':
			return ['win10-x64', 'publish', 'FSharpLanguageServer.exe'];

		case 'linux':
			return ['linux-x64', 'publish', 'FSharpLanguageServer'];

		case 'darwin':
			return ['osx.10.11-x64', 'publish', 'FSharpLanguageServer'];
	}

	throw `unsupported platform: ${platform}`; */
	return ['publish', 'FSharpLanguageServer.dll'];
}

function findInPath(binname: string) {
	let pathparts = process.env['PATH'].split(path.delimiter);
	for (let i = 0; i < pathparts.length; i++) {
		let binpath = path.join(pathparts[i], binname);
		if (fs.existsSync(binpath)) {
			return binpath;
		}
	}
	return null;
}
