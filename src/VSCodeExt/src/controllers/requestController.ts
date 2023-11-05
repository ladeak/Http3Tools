import { ExtensionContext, Range, TextDocument, ViewColumn, commands, window, ProgressLocation, workspace } from 'vscode';
import { RequestMetadata } from '../models/requestMetadata';
import { RequestStatusEntry } from '../utils/requestStatusBarEntry';
import { Selector } from '../utils/selector';
import { getCurrentTextDocument } from '../utils/workspaceUtility';
import { HttpResponseTextDocumentView } from '../views/httpResponseTextDocumentView';
import { HttpRequestParser } from '../utils/httpRequestParser';
import { SelectedRequest } from '../models/SelectedRequest';
import { RequestVariableCache } from '../utils/requestVariableCache';

export class RequestController {
    private _requestStatusEntry: RequestStatusEntry;
    private _textDocumentView: HttpResponseTextDocumentView;

    public constructor(context: ExtensionContext) {
        this._requestStatusEntry = new RequestStatusEntry();
        this._textDocumentView = new HttpResponseTextDocumentView();
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

        if (metadatas.has(RequestMetadata.ClientsCount) || metadatas.has(RequestMetadata.ClientsCount))
            await this.performanceMeasurementRequest(selectedRequest);
        else
            await this.sendRequest(selectedRequest, document);
    }

    public async performanceMeasurementRequest(selectedRequest: SelectedRequest) {
        const { text, metadatas } = selectedRequest;
        const name = metadatas.get(RequestMetadata.Name);

        // parse http request
        var parser = new HttpRequestParser(text);
        const performanceHttpRequest = await parser.parseHttpRequest(name);

        window.withProgress({
            location: ProgressLocation.Notification,
            title: "Running Performance Tests",
            cancellable: true
        }, async (progress, token) => {

            try {
                token.onCancellationRequested(() => {
                    CHttpModule.CHttpExt.cancel();
                });
                const CHttpModule = require('../chttp-win-x64/CHttpExtension.node');
                var response = await CHttpModule.CHttpExt.perfMeasureAsync(
                    name ? name : null,
                    !metadatas.has(RequestMetadata.NoRedirect),
                    !metadatas.has(RequestMetadata.NoCertificateValidation),
                    metadatas.has(RequestMetadata.KerberosAuth),
                    this.tryParseInt(metadatas.get(RequestMetadata.Timeout), 40),
                    performanceHttpRequest.method,
                    performanceHttpRequest.uri,
                    performanceHttpRequest.version,
                    performanceHttpRequest.headers,
                    performanceHttpRequest.content,
                    this.tryParseInt(metadatas.get(RequestMetadata.RequestCount), 100),
                    this.tryParseInt(metadatas.get(RequestMetadata.ClientsCount), 10),
                    (data: string) => progress.report({ message: data }));


                if (response == "" || response == "Cancelled") {
                    this._requestStatusEntry.updateStatus("Cancelled");
                    return;
                }

                this._textDocumentView.render(response);
                this._requestStatusEntry.updateStatus("Completed");
            } catch (reason: any) {
                this._requestStatusEntry.updateStatus("Error");
                if ("message" in reason)
                    window.showErrorMessage(reason.message);
                else
                    window.showErrorMessage("Command failed");
            }
        });
    }

    public async sendRequest(selectedRequest: SelectedRequest, document: TextDocument) {
        const { text, metadatas } = selectedRequest;
        const name = metadatas.get(RequestMetadata.Name);

        // parse http request
        var parser = new HttpRequestParser(text);
        const httpRequest = await parser.parseHttpRequest(name);

        try {
            const CHttpModule = require('../chttp-win-x64/CHttpExtension.node');
            var response = await CHttpModule.CHttpExt.sendRequestAsync(
                !metadatas.has(RequestMetadata.NoRedirect),
                !metadatas.has(RequestMetadata.NoCertificateValidation),
                metadatas.has(RequestMetadata.KerberosAuth),
                this.tryParseInt(metadatas.get(RequestMetadata.Timeout), 40),
                httpRequest.method,
                httpRequest.uri,
                httpRequest.version,
                httpRequest.headers,
                httpRequest.content);

            if (response == "" || response == "Cancelled") {
                this._requestStatusEntry.updateStatus("Cancelled");
                return;
            }

            if(metadatas.has(RequestMetadata.Name))
            RequestVariableCache.add(document, metadatas.get(RequestMetadata.Name)!, response);
            this._textDocumentView.render(response);
            this._requestStatusEntry.updateStatus("Completed");
        } catch (reason: any) {
            this._requestStatusEntry.updateStatus("Error");
            if ("message" in reason)
                window.showErrorMessage(reason.message);
            else
                window.showErrorMessage("Command failed");
        }
    }

    public tryParseInt(str: string | undefined, defaultValue: number): number {
        if (str == undefined)
            return defaultValue;

        let num = parseInt(str);
        if (isNaN(num))
            return defaultValue;
        else
            return num;
    }

    public dispose() {
        this._requestStatusEntry.dispose();
    }
}