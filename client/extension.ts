/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import { workspace, ExtensionContext } from 'vscode';
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from 'vscode-languageclient';

export function activate(context: ExtensionContext) {

	// The server is packaged as a standalone command
	let serverMain = context.asAbsolutePath(binName());
	
	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	let serverOptions: ServerOptions = {
		run : { command: serverMain, args: [], transport: TransportKind.stdio },
		debug : { command: serverMain, args: [], transport: TransportKind.stdio }
	}
	
	// Options to control the language client
	let clientOptions: LanguageClientOptions = {
		// Register the server for F# documents
		documentSelector: [{scheme: 'file', language: 'fsharp'}],
		synchronize: {
			// Synchronize the setting section 'languageServerExample' to the server
			configurationSection: 'fsharp',
			// Notify the server about file changes to F# project files contain in the workspace
			fileEvents: [
				workspace.createFileSystemWatcher('**/*.fsproj'),
				workspace.createFileSystemWatcher('**/project.assets.json')
			]
		}
	}
	
	// Create the language client and start the client.
	let disposable = new LanguageClient('fsharp', 'F# Language Server', serverOptions, clientOptions).start();
	
	// Push the disposable to the context's subscriptions so that the 
	// client can be deactivated on extension deactivation
	context.subscriptions.push(disposable);
}

function binName() {
	if (process.platform === 'win32')
		return path.join('src', 'Main', 'bin', 'Release', 'netcoreapp2.0', 'win10-x64', 'publish', 'Main.exe')
	else
		return path.join('src', 'Main', 'bin', 'Release', 'netcoreapp2.0', 'osx.10.11-x64', 'publish', 'Main')
}