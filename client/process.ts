/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import vscode = require("coc.nvim");

export class FsiProcess {

    public onExited: vscode.Event<void>;
    private onExitedEmitter = new vscode.Emitter<void>();

    private consoleTerminal: vscode.Terminal = undefined;
    private consoleCloseSubscription: vscode.Disposable;

    public log = vscode.workspace.createOutputChannel('fsharp-interactive')

    constructor(private title: string) {

        this.onExited = this.onExitedEmitter.event;
    }

    public async start() {

        if (this.consoleTerminal) {
            this.log.appendLine("F# REPL already started.")
            this.consoleTerminal.show(true)
            return
        }

        this.log.appendLine("F# REPL starting.")

        this.consoleTerminal = await vscode.workspace.createTerminal({
            name: this.title,
            shellPath: "dotnet",
            shellArgs: ["fsi", "--readline+"]
        })

        this.consoleCloseSubscription =
            vscode.workspace.onDidCloseTerminal(
                (terminal) => {
                    if (terminal === this.consoleTerminal) {
                        this.log.appendLine("F# REPL terminated or terminal UI was closed");
                        this.onExitedEmitter.fire();
                    }
                }, this);
    }

    public showConsole(preserveFocus: boolean) {
        if (this.consoleTerminal) {
            this.consoleTerminal.show(preserveFocus);
        }
    }

    public async eval(line: string) {
        if (this.consoleTerminal) {
            this.consoleTerminal.sendText(line)
        }
    }

    private sleep(time: number) {
        return new Promise((resolve, reject) => setTimeout(() => resolve(), time))
    }

    public async scrollToBottom() {
        this.consoleTerminal.show(false)
        await this.sleep(200)
        await vscode.workspace.nvim.command("wincmd w")
    }

    public dispose() {

        if (this.consoleCloseSubscription) {
            this.consoleCloseSubscription.dispose();
            this.consoleCloseSubscription = undefined;
        }

        if (this.consoleTerminal) {
            this.log.appendLine("Terminating F# REPL process...");
            this.consoleTerminal.dispose();
            this.consoleTerminal = undefined;
        }
    }
}
