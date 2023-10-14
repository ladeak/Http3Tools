import { ExtensionContext, Range, TextDocument, ViewColumn, commands, window, ProgressLocation, Webview } from 'vscode';
import { RequestMetadata } from '../models/requestMetadata';
import { RequestStatusEntry } from '../utils/requestStatusBarEntry';
import { Selector } from '../utils/selector';
import { getCurrentTextDocument } from '../utils/workspaceUtility';
import { ResponseWebView } from '../views/responseWebView';
import { DiffRequestParser } from '../utils/diffRequestParser';
import { SelectedRequest } from '../models/SelectedRequest';

export class DiffController {
    private _requestStatusEntry: RequestStatusEntry;
    private _view: ResponseWebView;

    public constructor(context: ExtensionContext) {
        this._requestStatusEntry = new RequestStatusEntry();
        this._view = new ResponseWebView();
    }

    public async run(range: Range) {
        this._requestStatusEntry.updateStatus("Working...", 'LaDeak-CHttp.cancelRequest');

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

        if (metadatas.has(RequestMetadata.Note)) {
            const userConfirmed = await window.showWarningMessage('Are you sure you want to send this request?', 'Yes', 'No');
            if (userConfirmed !== 'Yes') {
                return;
            }
        }

        await this.diffResults(selectedRequest);
    }

    public async diffResults(selectedRequest: SelectedRequest) {
        const { text, metadatas } = selectedRequest;

        var parser = new DiffRequestParser(text);
        const diffRequest = await parser.parse(text);
        if ("error" in diffRequest) {
            window.showErrorMessage(diffRequest.error);
            return;
        }
        try {
            const CHttpModule = require('../chttp-win-x86/CHttpExtension.node');
            var response = await CHttpModule.CHttpExt.getDiffAsync(diffRequest.file1, diffRequest.file2);
            this._view.render(response);
            this._requestStatusEntry.updateStatus("Completed");
        } catch (reason: any) {
            this._requestStatusEntry.updateStatus("Error");
            if ("message" in reason)
                window.showErrorMessage(reason.message);
            else
                window.showErrorMessage("Command failed");
        }
    }

    public dispose() {
        this._requestStatusEntry.dispose();
    }
}