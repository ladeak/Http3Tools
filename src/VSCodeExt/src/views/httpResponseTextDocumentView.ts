import { languages, Position, Range, TextDocument, ViewColumn, window, workspace } from 'vscode';

export class HttpResponseTextDocumentView {

    private doc?: TextDocument = undefined;

    public constructor() {
        workspace.onDidCloseTextDocument(e => {
            if (e == this.doc) {
                this.doc = undefined;
            }
        });
    }

    public async render(response: string) {
        const content = response;
        const language = 'markdown';
        let document: TextDocument;
        if (this.doc == undefined) {
            document = await workspace.openTextDocument({ language, content });
            this.doc = document;
            await window.showTextDocument(document, { viewColumn: ViewColumn.Beside, preserveFocus: true, preview: true });
        } else {
            document = this.doc;
            const editor = await window.showTextDocument(this.doc, { viewColumn: ViewColumn.Beside, preserveFocus: true, preview: true });
            editor.edit(edit => {
                const startPosition = new Position(0, 0);
                const endPosition = document.lineAt(document.lineCount - 1).range.end;
                edit.replace(new Range(startPosition, endPosition), content);
            });
        }
    }
}