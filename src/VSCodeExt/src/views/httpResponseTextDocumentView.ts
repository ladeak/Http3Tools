import { languages, Position, Range, TextDocument, ViewColumn, window, workspace } from 'vscode';

export class HttpResponseTextDocumentView {

    public async render(response: string) {
        const content = response;
        const language = 'markdown';
        let document: TextDocument;
        document = await workspace.openTextDocument({ language, content });
        await window.showTextDocument(document, { viewColumn: ViewColumn.Beside, preserveFocus: true, preview: true });
    }
}