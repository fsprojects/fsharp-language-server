import coc = require("coc.nvim")
import {
    Message,
    RequestType,
    RequestType0,
    NotificationType,
    NotificationHandler,
} from "coc.nvim/node_modules/vscode-jsonrpc"
import {
    DocumentSelector,
    DocumentFormattingRequest,
    DocumentRangeFormattingParams,
    DocumentRangeFormattingRequest,
    TextDocumentIdentifier,
    TextDocument,
    WorkspaceEdit as IWEdit,
} from "coc.nvim/node_modules/vscode-languageserver-protocol"
import {
    CodeLens,
    CancellationToken,
    CancellationTokenSource,
    Uri,
    Position,
    Range,
    Location,
    QuickPickItem,
    QuickPick,
    QuickPickOptions,
    TreeView,
    TreeItem,
    TreeItemCollapsibleState,
    TreeDataProvider,
    ViewColumn,

    EventEmitter,
    FormattingOptions,
    TextEdit,
    TextEditor,
    TextLine,
    TextEditorEdit,
    TextEditorRevealType,

    TextDocumentChangeEvent,
    SnippetString,
    EndOfLine,

} from "vscode"
import {
    ExtensionContext,
    CloseAction,
    ErrorAction,
    Executable,
    LanguageClient,
    LanguageClientOptions,

    Middleware,
    ResolveCodeLensSignature,
    RevealOutputChannelOn,
    StreamInfo,

    StatusBarItem,
    Disposable,
    ProviderResult,
    Event,

    TextDocumentContentProvider,
    DocumentFormattingEditProvider,
    DocumentRangeFormattingEditProvider,
    OnTypeFormattingEditProvider,
    Terminal,
    WorkspaceConfiguration,

    workspace,
    commands,
    diagnosticManager,
    disposeAll,
    events,
    extensions,
    languages,
    listManager,
    services,
    snippetManager,
    sources,

} from "coc.nvim";

enum ProgressLocation {
    Window
}

class WorkspaceEdit implements IWEdit {
    changes: {
        [uri: string]: TextEdit[];
    };
    set(uri: Uri, edits: TextEdit[]): any {
        this.changes[uri.toString()] = edits;
    }

}

import { TextDocument as VSTD } from "vscode"

class CocDocument implements VSTD {
    uri: Uri;
    fileName: string;
    isUntitled: boolean;
    languageId: string;
    version: number;
    isDirty: boolean;
    isClosed: boolean;
    save(): Thenable<boolean> {
        throw new Error("Method not implemented.");
    }
    eol: EndOfLine;
    lineCount: number;
    lineAt(line: number): TextLine;
    lineAt(position: Position): TextLine;
    lineAt(position: any) {
        return null;
    }
    offsetAt(position: Position): number {
        return this._doc.getOffset(position.line, position.character);
    }
    positionAt(offset: number): Position {
        throw new Error("Method not implemented.");
    }
    getText(range?: Range): string {
        throw new Error("Method not implemented.");
    }
    getWordRangeAtPosition(position: Position, regex?: RegExp): Range {
        throw new Error("Method not implemented.");
    }
    validateRange(range: Range): Range {
        throw new Error("Method not implemented.");
    }
    validatePosition(position: Position): Position {
        throw new Error("Method not implemented.");
    }

    _doc: coc.Document;
    constructor(doc: coc.Document) {
        this._doc = doc;
    }
}

class Editor implements TextEditor {
    private _doc: coc.Document;

    constructor(doc: coc.Document) {
        this._doc = doc;
    }

    selections: import("vscode").Selection[];
    visibleRanges: Range[];
    options: import("vscode").TextEditorOptions;
    viewColumn?: ViewColumn;
    insertSnippet(snippet: import("vscode").SnippetString, location?: Range | Position | Range[] | Position[], options?: { undoStopBefore: boolean; undoStopAfter: boolean; }): Thenable<boolean> {
        throw new Error("Method not implemented.");
    }
    setDecorations(decorationType: import("vscode").TextEditorDecorationType, rangesOrOptions: Range[] | import("vscode").DecorationOptions[]): void {
        throw new Error("Method not implemented.");
    }
    revealRange(range: Range, revealType?: TextEditorRevealType): void {
        throw new Error("Method not implemented.");
    }
    show(column?: ViewColumn): void {
        throw new Error("Method not implemented.");
    }
    hide(): void {
        throw new Error("Method not implemented.");
    }

    get selection() {
        //TODO
        return null;
    }

    get document(): VSTD {
        return new CocDocument(this._doc);
    }

    get textDocument(): TextDocument {
        return this._doc.textDocument;
    }

    public async edit(cmd: (builder: TextEditorEdit) => void, options?: { undoStopBefore: boolean, undoStopAfter: boolean }): Promise<boolean> {
        //TODO
        await this._doc.applyEdits(coc.workspace.nvim, []);
        return true;
    }
}

class VSCodeWindowCompat {

    _term: Terminal = null;

    createOutputChannel(ch: string): any {
        return coc.workspace.createOutputChannel(ch);
    }
    createTreeView<T>(arg0: string, arg1: any): any {
        //coc.listManager.
        // TODO ^^^
    }

    showInputBox(prompt: any): Promise<any> {
        return coc.workspace.requestInput(prompt.placeHolder);
    }

    async showQuickPick<T extends QuickPickItem>(items: T[], args?: QuickPickOptions, token?: CancellationToken): Promise<T>;
    async showQuickPick(items: string[], args?: QuickPickOptions, token?: CancellationToken): Promise<string>;
    async showQuickPick(items: any[], args?: QuickPickOptions, token?: CancellationToken): Promise<any> {
        let xs = (typeof items[0] === "string") ? items : items.map(x => x.label);
        let num = await coc.workspace.showQuickpick(xs, args.placeHolder);
        return items[num];
    }

    async showErrorMessage(msg: string, action?: string) {
        coc.workspace.showMessage(msg, 'error');
        if (action != null) {
            return await coc.workspace.showPrompt(action);
        }
    }

    async showWarningMessage(msg: string, action?: string): Promise<boolean> {
        coc.workspace.showMessage(msg, 'warning');
        if (action != null) {
            return await coc.workspace.showPrompt(action);
        }
    }

    showInformationMessage(msg: string) {
        return new Promise((_) => coc.workspace.showMessage(msg));
    }

    withProgress(args: any, action: ((progress: any) => Promise<any>)) {
        //TODO
    }

    //public showTextDocument(doc: VSTD): Promise<Editor> {
    //    // TODO
    //    throw 0;
    //}

    showTextDocument(doc: coc.Document, options?: { preview: boolean }): Promise<Editor> {
        throw 0;
    }

    setStatusBarMessage(message: string, timeout?: number): any {
        throw new Error("Method not implemented.");
    }

    get visibleTextEditors(): Editor[] {
        return coc.workspace.documents.map(d => new Editor(d));
    }

    createTerminal(title: string, shell: string, args: string[]) {
        return coc.workspace.createTerminal({
            name: title,
            shellPath: shell,
            shellArgs: args,
            //cwd:
        }).then(term => {
            this._term = term;
            return term;
        });
    }

    get activeTerminal() {
        return this._term;
    }

    onDidCloseTerminal(arg0: (terminal: Terminal) => void): Disposable {
        return coc.workspace.onDidCloseTerminal(arg0);
    }

    get activeTextEditor() {
        return coc.workspace.document.then(doc => new Editor(doc));
    }
}

const window = new VSCodeWindowCompat();
const version = coc.workspace.version;

export {
    // coc types
    ExtensionContext,
    CloseAction,
    ErrorAction,
    Executable,
    LanguageClient,
    LanguageClientOptions,

    Middleware,
    ResolveCodeLensSignature,
    RevealOutputChannelOn,
    StreamInfo,

    StatusBarItem,
    Disposable,
    ProviderResult,
    Event,

    TextDocumentContentProvider,
    DocumentFormattingEditProvider,
    DocumentRangeFormattingEditProvider,
    OnTypeFormattingEditProvider,
    Terminal,
    WorkspaceConfiguration,


    // vscode
    CodeLens,
    CancellationToken,
    CancellationTokenSource,
    Message,
    RequestType,
    RequestType0,
    NotificationType,
    NotificationHandler,

    Uri,
    Position,
    Range,
    Location,
    QuickPickItem,
    QuickPick,
    QuickPickOptions,
    EventEmitter,
    ViewColumn,
    TreeView,
    TreeItem,
    TreeItemCollapsibleState,
    TreeDataProvider,
    FormattingOptions,
    TextDocumentChangeEvent,
    SnippetString,
    EndOfLine,


    TextEdit,
    TextEditor,
    TextLine,

    TextDocument,
    TextDocumentIdentifier,

    DocumentSelector,
    DocumentFormattingRequest,
    DocumentRangeFormattingParams,
    DocumentRangeFormattingRequest,


    // coc namespaces
    workspace,
    commands,
    diagnosticManager,
    disposeAll,
    events,
    extensions,
    languages,
    listManager,
    services,
    snippetManager,
    sources,

    // compat
    window,
    version,
    WorkspaceEdit,
    ProgressLocation
}