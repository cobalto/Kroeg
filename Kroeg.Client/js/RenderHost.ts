import { TemplateRenderer, TemplateService, RenderResult } from "./TemplateService";
import { EntityStore, StoreActivityToken } from "./EntityStore";
import { ASObject } from "./AS";

export class RenderHost {
    private _lastResult: RenderResult;
    private _object: ASObject;
    private _id: string;
    private _template: string;
    private _dom: HTMLDivElement;
    private _storeActivityToken: StoreActivityToken;
    private _subrender: RenderHost[] = [];

    public get id(): string { return this._id; }
    public set id(value: string) { this._id = value; this.update(); }

    public get template(): string { return this._template; }
    public set template(value: string) { this._template = value; this.render(); }

    constructor(private renderer: TemplateRenderer, private store: EntityStore, id: string, template: string, dom?: HTMLDivElement) {
        this._dom = dom != null ? dom : document.createElement("div");
        this._id = id;
        this._template = template;
        this.update();
    }

    public async update() {
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);

        const handlers: {[name: string]: (oldValue: ASObject, newValue: ASObject) => void} = {};
        handlers[this._id] = this._reupdate.bind(this);
        this._storeActivityToken = this.store.register(handlers);

        this._object = await this.store.get(this._id);
        this.render();
    }

    public deregister() {
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);
    }

    public get element(): HTMLDivElement { return this._dom; }

    private _reupdate(old: ASObject, newObject: ASObject) {
        this._object = newObject;
        this.render();
    }

    private static _counter = 0;

    private async render(): Promise<void> {
        if (this._object == null) return;

        for (let subrender of this._subrender)
            subrender.deregister();
        this._subrender.splice(0);

        const result = await this.renderer.render(this._template, this._object);
        let resultText = result.result[0];
        let counterStart = RenderHost._counter;
        for (var i = 1; i < result.result.length; i++) {
            resultText += `<div id="_renderhost_holder_${RenderHost._counter}">${JSON.stringify(result.subRender[i - 1])}</div>`;
            RenderHost._counter++;
            resultText += result.result[i];
        }

        this._dom.innerHTML = resultText;
        for (let i = 0; i < result.subRender.length; i++) {
            let subrender = result.subRender[i];
            let holder = document.getElementById(`_renderhost_holder_${counterStart + i}`) as HTMLDivElement;
            let host = new RenderHost(this.renderer, this.store, subrender.id, subrender.template, holder);
            this._subrender.push(host);
        }
        this._lastResult = result;
    }
}