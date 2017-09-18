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
    public set id(value: string) { this._id = value; this.update(true); }

    public get template(): string { return this._template; }
    public set template(value: string) { this._template = value; this.render(); }

    constructor(private renderer: TemplateRenderer, private store: EntityStore, id: string, template: string, dom?: HTMLDivElement, private _parent?: RenderHost) {
        this._dom = dom != null ? dom : document.createElement("div");
        this._id = id;
        this._template = template;
        this.update(true);
    }

    public async update(reload: boolean = false) {
        if (this._storeActivityToken != null)
            this.store.deregister(this._storeActivityToken);

        if (reload) this._dom.innerHTML = '<span class="renderhost_loading">🍺</span>';        

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

    public async render(): Promise<void> {
        if (this._object == null) return;

        for (let subrender of this._subrender)
            subrender.deregister();
        this._subrender.splice(0);

        const result = await this.renderer.render(this._template, this._object);
        let resultText = result.result[0];
        let counterStart = RenderHost._counter;
        for (var i = 1; i < result.result.length; i++) {
            let wrap = this.renderer.getWrap(result.subRender[i - 1].template);
            if (wrap == null) wrap = "div style=\"border: 1px solid red\"";
            let endwrap = wrap.split(' ')[0];
            resultText += `<${wrap} id="_renderhost_holder_${RenderHost._counter}">${JSON.stringify(result.subRender[i - 1])}</${endwrap}>`;
            RenderHost._counter++;
            resultText += result.result[i];
        }

        this._dom.innerHTML = resultText;
        for (let i = 0; i < result.subRender.length; i++) {
            let subrender = result.subRender[i];
            let holder = document.getElementById(`_renderhost_holder_${counterStart + i}`) as HTMLDivElement;
            if (holder == null) return; // race conditions in JS :D
            holder.dataset.id = subrender.id;
            holder.dataset.template = subrender.template;
            let host = new RenderHost(this.renderer, this.store, subrender.id, subrender.template, holder, this);
            this._subrender.push(host);
        }
        this._lastResult = result;
    }
}