import { Session } from "./Session";
import { ASObject } from "./AS";
import * as jsonld from "jsonld";


export type ChangeHandler = (oldValue: ASObject, newValue: ASObject) => void;

export class StoreActivityToken {
    public items: {[id: string]: ChangeHandler[]} = {};

    public addToHandler(id: string, handler: ChangeHandler) {
        if (!(id in this.items)) this.items[id] = [];
        this.items[id].push(handler);
    }
}

export class EntityStore {
    private _handlers: {[id: string]: ChangeHandler[]} = {};
    private _cache: {[id: string]: ASObject} = {};

    constructor(private session: Session) {}

    private _addToHandler(id: string, handler: ChangeHandler) {
        if (!(id in this._handlers)) this._handlers[id] = [];
        this._handlers[id].push(handler);
    }

    private _removeFromHandler(id: string, handler: ChangeHandler) {
        this._handlers[id].splice(this._handlers[id].indexOf(handler), 1);
    }
    
    public register(handlers: {[id: string]: ChangeHandler}, existing?: StoreActivityToken): StoreActivityToken {
        if (existing == null) existing = new StoreActivityToken();
        for (let id in handlers) {
            this._addToHandler(id, handlers[id]);
            existing.addToHandler(id, handlers[id]);
        }

        return existing;
    }

    public deregister(handler: StoreActivityToken) {
        for (let id in handler.items) {
            for (let item of handler.items[id])
                this._removeFromHandler(id, item);
        }

        handler.items = {};
    }

    private _addToCache(id: string, obj: ASObject) {
        let prev: ASObject = undefined
        if (id in this._cache)
            prev = this._cache[id];

        this._cache[id] = obj;

        if (id in this._handlers)
            for (let handler of this._handlers[id])
                handler(prev, obj);
    }

    private async loadDocument(url: string, callback: (err: Error | null, documentObject: jsonld.DocumentObject) => void) {
        try {
            let response = await this.session.authFetch(url);
            let data = await response.json();
            callback(null, data);
        } catch (e) {
            callback(e, null);
        }
    }

    public async get(id: string, cache: boolean = true): Promise<ASObject> {
        if (id in this._cache && cache)
            return this._cache[id];

        let processor = new jsonld.JsonLdProcessor();

        let data = await this.session.getObject(id);
        let flattened = await processor.flatten(data, data as any) as any;

        for (let item of flattened["@graph"]) {
            this._addToCache(item["id"], item);
        }
        return this._cache[id];
    }
}