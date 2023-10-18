import * as vscode from 'vscode';
import { commands, ExtensionContext, languages, Range, TextDocument, Uri, window, workspace } from 'vscode';
import { RequestController } from './controllers/requestController';
import { DiffController } from './controllers/diffController';
import { HttpCodeLensProvider } from './providers/httpCodeLensProvider'
import { RequestVariableHoverProvider } from './providers/requestVariableHoverProvider';
export function activate(context: vscode.ExtensionContext) {

    const requestController = new RequestController(context);
	const diffController = new DiffController(context);
	let sendRequest = vscode.commands.registerCommand('LaDeak-CHttp.sendRequest', ((document: TextDocument, range: Range) => requestController.run(range)));
	let cancelRequest = vscode.commands.registerCommand('LaDeak-CHttp.cancelRequest', ((document: TextDocument, range: Range) => 
	{
		const CHttpModule = require('./chttp-win-x86/CHttpExtension.node');
        CHttpModule.CHttpExt.cancel();
	}));
	let diff = vscode.commands.registerCommand('LaDeak-CHttp.diff', ((document: TextDocument, range: Range) => diffController.run(range)));

	const documentSelector = [
        { language: 'chttp', scheme: '*' }
    ];

	context.subscriptions.push(languages.registerCodeLensProvider(documentSelector, new HttpCodeLensProvider()));
	context.subscriptions.push(languages.registerHoverProvider(documentSelector, new RequestVariableHoverProvider()));
	context.subscriptions.push(sendRequest);
	context.subscriptions.push(cancelRequest);
	context.subscriptions.push(diff);
}

// This method is called when your extension is deactivated
export function deactivate() { }
