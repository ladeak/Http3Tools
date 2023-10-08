import { languages, Position, Range, TextDocument, ViewColumn, window, workspace } from 'vscode';

export class HttpResponseTextDocumentView {

    protected readonly documents: TextDocument[] = [];

    public constructor() {
        workspace.onDidCloseTextDocument(e => {
            const index = this.documents.indexOf(e);
            if (index !== -1) {
                this.documents.splice(index, 1);
            }
        });
    }

    public async render(response: string, column?: ViewColumn) {
        const content = response;
        const language = 'markdown';
        let document: TextDocument;
        if (true || this.documents.length === 0) {
            document = await workspace.openTextDocument({ language, content });
            this.documents.push(document);
            await window.showTextDocument(document, { viewColumn: column, preserveFocus: true, preview: false });
        }
    }
}