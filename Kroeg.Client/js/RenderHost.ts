import { TemplateRenderer, TemplateService, RenderResult } from "./TemplateService";
import { EntityStore, StoreActivityToken } from "./EntityStore";
import { ASObject } from "./AS";

export class RenderHost {
    private _lastResult: RenderResult;
    private _id: string;
    private _template: string;
    private _dom: HTMLDivElement;
    private _storeActivityToken: StoreActivityToken;

    public get id(): string { return this._id; }
    public set id(value: string) { this._id = value; this.render(); }

    public get template(): string { return this._template; }
    public set template(value: string) { this._template = value; this.render(); }

    constructor(private renderer: TemplateRenderer, private store: EntityStore, id: string, template: string) {
        this._dom = document.createElement("div");
        this._id = id;
        this._template = template;
        this.render();
    }

    public get element(): HTMLDivElement { return this._dom; }

    private _reupdate(old: ASObject, newObject: ASObject) {
        this.render();
    }

    private async render(): Promise<void> {
        const result = await this.renderer.render(this._template, this._id);
        this._dom.innerHTML = result.result;
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);

        const handlers: {[name: string]: (oldValue: ASObject, newValue: ASObject) => void} = {};
        for (let id of result.usedIds)
            handlers[id] = this._reupdate;

        this._storeActivityToken = this.store.register(handlers);
        this._lastResult = result;
    }
}