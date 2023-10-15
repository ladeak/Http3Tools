import { Event, EventEmitter, TextDocument } from "vscode";
import { DocumentCache } from '../models/documentCache';

type RequestVariableEvent = {
    name: string;
    document: TextDocument;
};

export class RequestVariableCache {
    private static cache = new DocumentCache<Map<string, string>>(true);

    private static readonly eventEmitter = new EventEmitter<RequestVariableEvent>();

    public static get onDidCreateNewRequestVariable(): Event<RequestVariableEvent> {
        return this.eventEmitter.event;
    }

    public static add(document: TextDocument, name: string, response: string) {
        if (!this.cache.has(document)) {
            this.cache.set(document, new Map<string, string>());
        }

        this.cache.get(document)!.set(name, response);
        this.eventEmitter.fire({ name, document });
    }

    public static has(document: TextDocument, name: string): boolean {
        return this.cache.has(document) && this.cache.get(document)!.has(name);
    }

    public static get(document: TextDocument, name: string): string | undefined {
        return this.cache.get(document)?.get(name);
    }
}