import { ExtensionContext, Range, TextDocument, ViewColumn, window } from 'vscode';
import { RequestMetadata } from '../models/requestMetadata';
import { RequestStatusEntry } from '../utils/requestStatusBarEntry';
import { Selector } from '../utils/selector';
import { getCurrentTextDocument } from '../utils/workspaceUtility';
import { HttpResponseTextDocumentView } from '../views/httpResponseTextDocumentView';
import { HttpRequestParser } from '../utils/httpRequestParser';

export class RequestController {
    public _requestStatusEntry: RequestStatusEntry;
    private _textDocumentView: HttpResponseTextDocumentView;

    public constructor(context: ExtensionContext) {
        this._requestStatusEntry = new RequestStatusEntry();
        this._textDocumentView = new HttpResponseTextDocumentView();
    }

    public async run(range: Range) {
        this._requestStatusEntry.update("Pending");

        const editor = window.activeTextEditor;
        const document = getCurrentTextDocument();
        if (!editor || !document) {
            return;
        }

        const selectedRequest = await Selector.getRequest(editor, range);
        if (!selectedRequest) {
            return;
        }

        const { text, metadatas } = selectedRequest;
        const name = metadatas.get(RequestMetadata.Name);

        if (metadatas.has(RequestMetadata.Note)) {
            const note = name ? `Are you sure you want to send the request "${name}"?` : 'Are you sure you want to send this request?';
            const userConfirmed = await window.showWarningMessage(note, 'Yes', 'No');
            if (userConfirmed !== 'Yes') {
                return;
            }
        }

        // parse http request
        var parser = new HttpRequestParser(text);
        const performanceHttpRequest = await parser.parseHttpRequest(name);

        const CHttpModule = require('../bin/CHttpExtension.node');

        var response = await CHttpModule.CHttpExt.run(performanceHttpRequest.enableRedirects,
            performanceHttpRequest.enableCertificateValidation,
            performanceHttpRequest.timeout,
            performanceHttpRequest.method,
            performanceHttpRequest.uri,
            performanceHttpRequest.version,
            performanceHttpRequest.headers,
            performanceHttpRequest.content,
            performanceHttpRequest.requestCount,
            performanceHttpRequest.clientsCount,
            (data:string) => this._requestStatusEntry.update(data, 'LaDeakCHttpVSCodeExt.cancelRequest'));

        try {
            const activeColumn = window.activeTextEditor!.viewColumn;
            const previewColumn = activeColumn == 1 ? ((activeColumn as number) + 1) as ViewColumn : activeColumn;
            this._textDocumentView.render(response, previewColumn);
        } catch (reason) {
            this._requestStatusEntry.update("Error");
            window.showErrorMessage("Failed to render response");
        }
    }

    public dispose() {
        this._requestStatusEntry.dispose();
    }
}