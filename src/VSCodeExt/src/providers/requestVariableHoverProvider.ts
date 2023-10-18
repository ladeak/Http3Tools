import { CancellationToken, Hover, HoverProvider, MarkdownString, MarkedString, Position, TextDocument, Range } from 'vscode';
import { RequestVariableProvider } from '../utils/httpVariableProviders/requestVariableProvider';

export class RequestVariableHoverProvider implements HoverProvider {

    private readonly requestVariableReferenceRegex = /\{{2}(\w+)\.(response|request)?(\.body(\..*?)?|\.headers(\.[\w-]+)?)?\}{2}/;

    public async provideHover(document: TextDocument, position: Position, token: CancellationToken): Promise<Hover | undefined> {
        const wordRange = this.getRequestVariableReferencePathRange(document, position);
        if (!wordRange) {
            return undefined;
        }

        const fullPath = document.getText(wordRange);

        const { name, value, warning, error } = await RequestVariableProvider.Instance.get(fullPath, document);
        if (!error && !warning) {
            const contents: MarkedString[] = [];
            if (value) {
                contents.push(typeof value === 'string' ? value : { language: 'json', value: JSON.stringify(value, null, 2) });
            }
            contents.push('Request Variable');
            return new Hover(contents, wordRange);
        }

        return undefined;
    }

    public getRequestVariableReferencePathRange(document: TextDocument, position: Position): Range | undefined {
        const wordRange = document.getWordRangeAtPosition(position, this.requestVariableReferenceRegex);
        if (!wordRange) {
            return undefined;
        }

        // Remove leading and trailing curly braces
        const start = wordRange.start.with({ character: wordRange.start.character + 2 });
        const end = wordRange.end.with({ character: wordRange.end.character - 2 });
        return wordRange.with(start, end);
    }
}