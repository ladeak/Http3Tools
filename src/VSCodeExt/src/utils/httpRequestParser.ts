import { EOL } from 'os';
import { Stream } from 'stream';
import { MimeUtility } from './mimeUtility';
import { getHeader, removeHeader } from './misc';
import { parseRequestHeaders } from './requestParserUtil';


enum ParseState {
    URL,
    Header,
    Body,
}

export class PerformanceBehavior {
    public constructor(
        public method: string,
        public uri: string,
        public version: string,
        public headers: Iterable<string>,
        public content: string) { }
}

export class HttpRequestParser {
    private readonly defaultMethod = 'GET';
    private readonly queryStringLinePrefix = /^\s*[&\?]/;
    private readonly inputFileSyntax = /^<(?:(?<processVariables>@)(?<encoding>\w+)?)?\s+(?<filepath>.+?)\s*$/;

    public constructor(private readonly requestRawText: string) {
    }

    public async parseHttpRequest(name?: string): Promise<PerformanceBehavior> {
        // parse follows http://www.w3.org/Protocols/rfc2616/rfc2616-sec5.html
        // split the request raw text into lines
        const lines: string[] = this.requestRawText.split(EOL);
        const requestLines: string[] = [];
        const headersLines: string[] = [];
        const bodyLines: string[] = [];

        let state = ParseState.URL;
        let currentLine: string | undefined;
        while ((currentLine = lines.shift()) !== undefined) {
            const nextLine = lines[0];
            switch (state) {
                case ParseState.URL:
                    requestLines.push(currentLine.trim());
                    if (nextLine === undefined
                        || this.queryStringLinePrefix.test(nextLine)) {
                        // request with request line only
                    } else if (nextLine.trim()) {
                        state = ParseState.Header;
                    } else {
                        // request with no headers but has body
                        // remove the blank line before the body
                        lines.shift();
                        state = ParseState.Body;
                    }
                    break;
                case ParseState.Header:
                    headersLines.push(currentLine.trim());
                    if (nextLine?.trim() === '') {
                        // request with no headers but has body
                        // remove the blank line before the body
                        lines.shift();
                        state = ParseState.Body;
                    }
                    break;
                case ParseState.Body:
                    bodyLines.push(currentLine);
                    break;
            }
        }

        // parse request line
        const requestLine = this.parseRequestLine(requestLines.map(l => l.trim()).join(''));

        // parse headers lines
        const headers = parseRequestHeaders(headersLines);

        // let underlying chttp recalculate the content length
        removeHeader(headers, 'content-length');

        // if Host header provided and url is relative path, change to absolute url
        const host = getHeader(headers, 'Host');
        if (host && requestLine.url[0] === '/') {
            const [, port] = host.toString().split(':');
            const scheme = port === '443' || port === '8443' ? 'https' : 'http';
            requestLine.url = `${scheme}://${host}${requestLine.url}`;
        }

        return new PerformanceBehavior(requestLine.method, requestLine.url, requestLine.httpVersion, headersLines, bodyLines.join(EOL));
    }

    private parseRequestLine(line: string): { method: string, url: string, httpVersion: string } {
        // Request-Line = Method SP Request-URI SP HTTP-Version CRLF
        let method: string;
        let url: string;
        let httpVersion: string = '2';

        let match: RegExpExecArray | null;
        if (match = /^(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS|CONNECT|TRACE)\s+/i.exec(line)) {
            method = match[1];
            url = line.substr(match[0].length);
        } else {
            // Only provides request url
            method = this.defaultMethod;
            url = line;
        }

        url = url.trim();

        if (match = /\s+HTTP\/.*$/i.exec(url)) {
            if (url.length > match.index + 6)
                httpVersion = url.substr(match.index + 6, url.length - match.index - 6)
            url = url.substr(0, match.index);
        }

        return { method, url, httpVersion };
    }
}