import { EOL } from 'os';
import { ResolveErrorMessage, ResolveResult, ResolveState, ResolveWarningMessage } from "../models/httpVariableResolveResult";
import { MimeUtility } from './mimeUtility';
import { getContentType, getHeader, isJSONString } from './misc';
import { RequestHeaders } from '../models/base';
import { parseRequestHeaders } from './requestParserUtil';

const { JSONPath } = require('jsonpath-plus');

const requestVariablePathRegex: RegExp = /^(\w+)(?:\.(response)(?:\.(body|headers)(?:\.(.*))?)?)?$/;

type HttpPart = 'headers' | 'body';

export class RequestVariableCacheValueProcessor {
    public static resolveRequestVariable(value: string | undefined, path: string): ResolveResult {
        if (!value || !path) {
            return { state: ResolveState.Error, message: ResolveErrorMessage.NoRequestVariablePath };
        }

        const matches = path.match(requestVariablePathRegex);

        if (!matches) {
            return { state: ResolveState.Error, message: ResolveErrorMessage.InvalidRequestVariableReference };
        }

        const [, , type, httpPart, nameOrPath] = matches;

        if (!type) {
            return { state: ResolveState.Warning, value, message: ResolveWarningMessage.MissingRequestEntityName };
        }
        if (!httpPart) {
            return { state: ResolveState.Warning, value: value, message: ResolveWarningMessage.MissingRequestEntityPart };
        }

        return this.resolveHttpPart(value, httpPart as HttpPart, nameOrPath);
    }

    private static getResponseParts(http: string): { headers: RequestHeaders, body: string } {
        const lines: string[] = http.split(EOL);
        let currentLine: string | undefined;
        let isHeader: boolean = true;
        let headersLines: string[] = [];
        let bodyLines: string[] = [];

        // Skip to first line of VerboseConsoleWriter
        if (lines.shift() == undefined)
            return { headers: {}, body: "" };
        while ((currentLine = lines.shift()) !== undefined) {
            if (isHeader && currentLine.length == 0) {
                isHeader = false;
            }
            else {
                if (isHeader)
                    headersLines.push(currentLine.trim());
                else
                    bodyLines.push(currentLine.trim());
            }
        }

        // Remove the summary line of VerboseConsoleWriter
        if (bodyLines.length > 1)
            bodyLines = bodyLines.slice(0, bodyLines.length - 2);

        return { headers: parseRequestHeaders(headersLines), body: bodyLines.join() };
    }

    private static resolveHttpPart(http: string, httpPart: HttpPart, nameOrPath?: string): ResolveResult {
        const { headers, body } = RequestVariableCacheValueProcessor.getResponseParts(http);
        if (httpPart === "body") {
            if (!nameOrPath) {
                return { state: ResolveState.Warning, value: body, message: ResolveWarningMessage.MissingBodyPath };
            }

            // Make '*' as the wildcard to fetch the whole body regardless of the content-type
            if (nameOrPath === '*') {
                return { state: ResolveState.Success, value: body };
            }

            const contentTypeHeader = getContentType(headers);
            if (MimeUtility.isJSON(contentTypeHeader) || (MimeUtility.isJavaScript(contentTypeHeader) && isJSONString(body as string))) {
                const parsedBody = JSON.parse(body as string);
                return this.resolveJsonHttpBody(parsedBody, nameOrPath);
            } else {
                return { state: ResolveState.Warning, value: body, message: ResolveWarningMessage.UnsupportedBodyContentType };
            }

        } else {
            if (!nameOrPath) {
                return { state: ResolveState.Warning, value: headers, message: ResolveWarningMessage.MissingHeaderName };
            }

            const value = getHeader(headers, nameOrPath);
            if (!value) {
                return { state: ResolveState.Warning, message: ResolveWarningMessage.IncorrectHeaderName };
            } else {
                return { state: ResolveState.Success, value };
            }
        }
    }

    private static resolveJsonHttpBody(body: any, path: string): ResolveResult {
        try {
            const result = JSONPath({ path, json: body });
            const value = typeof result[0] === 'string' ? result[0] : JSON.stringify(result[0]);
            if (!value) {
                return { state: ResolveState.Warning, message: ResolveWarningMessage.IncorrectJSONPath };
            } else {
                return { state: ResolveState.Success, value };
            }
        } catch {
            return { state: ResolveState.Warning, message: ResolveWarningMessage.InvalidJSONPath };
        }
    }
}