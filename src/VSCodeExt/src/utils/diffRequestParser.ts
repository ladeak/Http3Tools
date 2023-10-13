import { EOL } from 'os';

export type DiffParameters = {
    file1: string,
    file2: string,
}

export type FailedParse = {error: string}

export class DiffRequestParser {
    private readonly defaultMethod = 'GET';
    private readonly queryStringLinePrefix = /^\s*[&\?]/;
    private readonly inputFileSyntax = /^<(?:(?<processVariables>@)(?<encoding>\w+)?)?\s+(?<filepath>.+?)\s*$/;

    public constructor(private readonly requestRawText: string) {
    }

    public async parse(name?: string): Promise<DiffParameters | FailedParse> {
        // parse follows http://www.w3.org/Protocols/rfc2616/rfc2616-sec5.html
        // split the request raw text into lines
        const requestLine: string = this.requestRawText.split(EOL)[0];
        const parts: string[] = requestLine.split(" ");

        if(parts.length != 3 || parts[0] != "DIFF")
        {
            const result: FailedParse = {error: "Command must contain two name requests"};
            return result;
        }
        const parseResult: DiffParameters = { file1: parts[1], file2: parts[2] };
        return parseResult;
    }
}