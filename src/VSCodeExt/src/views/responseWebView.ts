import { languages, Position, Range, TextDocument, ViewColumn, window, workspace, WebviewPanel } from 'vscode';

export class ResponseWebView {

    private panel?: WebviewPanel = undefined;

    public constructor() {
    }

    public async render(response: string) {
        if (this.panel == undefined) {
            this.panel = window.createWebviewPanel(
                "chttp-diff-response",
                "diff",
                { viewColumn: ViewColumn.Beside, preserveFocus: true },
                {
                    enableFindWidget: false,
                    enableScripts: false,
                    retainContextWhenHidden: false
                });
        }
        this.panel.webview.html = this.getWebviewContent(response);
        this.panel.reveal();
    }

    private getWebviewContent(response: string) : string {
        return `<!DOCTYPE html>
      <html lang="en">
      <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>diff</title>
      </head>
      <body><pre style="max-width: 580px;white-space: pre-wrap;word-wrap: break-word;"><code>
        ${response}</code></pre>
      </body>
      </html>`;
    }
}
