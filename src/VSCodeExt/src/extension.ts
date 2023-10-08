import * as vscode from 'vscode';
import { commands, ExtensionContext, languages, Range, TextDocument, Uri, window, workspace } from 'vscode';
import { RequestController } from './controllers/requestController';
import { HttpCodeLensProvider } from './providers/httpCodeLensProvider'
export function activate(context: vscode.ExtensionContext) {

    const requestController = new RequestController(context);
	let sendRequest = vscode.commands.registerCommand('LaDeakCHttpVSCodeExt.sendRequest', ((document: TextDocument, range: Range) => requestController.run(range)));
	let cancelRequest = vscode.commands.registerCommand('LaDeakCHttpVSCodeExt.cancelRequest', ((document: TextDocument, range: Range) => 
	{
		requestController._requestStatusEntry.update("Canceling...");
		const CHttpModule = require('../bin/CHttpExtension.node');
        CHttpModule.CHttpExt.cancel();
	}));

	const documentSelector = [
        { language: 'http', scheme: '*' }
    ];

	context.subscriptions.push(languages.registerCodeLensProvider(documentSelector, new HttpCodeLensProvider()));
	context.subscriptions.push(sendRequest);
	context.subscriptions.push(cancelRequest);
}

// This method is called when your extension is deactivated
export function deactivate() { }
