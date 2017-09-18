import { EntityStore } from "./EntityStore";
import * as AS from "./AS";

export class TemplateItem {
    public type: "command" | "if" | "while" | "else" | "jump" | "end" | "text" | "wrap";
    public data: string;
    public offset: number;
}

export type Template = TemplateItem[];

export class TemplateService {
    private _templatePromise: Promise<{[item: string]: Template}>

    constructor() {
        this._templatePromise = this._getTemplates();
    }

    private async _getTemplates(): Promise<{[item: string]: Template}> {
        const result = await fetch("http://localhost:5000/settings/templates");
        return await result.json();
    }

    public getTemplates(): Promise<{[item: string]: Template}> {
        return this._templatePromise;
    }
}

export class RenderResult {
    public result: string[] = [];
    public subRender: {id: string, template: string}[] = [];
}

class Registers {
    public load: string[];
    public accumulator: number;
}

export class TemplateRenderer {
    private templates: {[item: string]: Template};

    public constructor(private templateService: TemplateService, private entityStore: EntityStore) {
    }

    public async prepare() {
        this.templates = await this.templateService.getTemplates();
    }

    public getWrap(template: string): string {
        if (template.startsWith("{")) {
            return JSON.parse(template).wrap;
        }
        if (!(template in this.templates)) return null;
        if (this.templates[template][0].type != "wrap") return null;
        return this.templates[template][0].data;
    }

    private _parseCondition(object: AS.ASObject, text: string, reg: Registers): boolean {
        if (text == "next") {
            reg.accumulator++;
            return reg.accumulator < reg.load.length;
        } else if (text == "client") return true;

        const split = text.split(' ');
        if (split.length == 2) {
            if (text == "is Activity") return "actor" in object;
            else if (text == "is Collection") return AS.containsAny(object, "type", ["Collection", "OrderedCollection"]);
            else if (text == "is CollectionPage") return AS.containsAny(object, "type", ["CollectionPage", "OrderedCollectionPage"]);
            else if (split[0] == "is") return AS.contains(object, "type", split[1]);
            else if (split[0] == "has") return AS.get(object, split[1]).length > 0;
            return false;
        }

        const value = split[0];
        const arr = AS.get(object, split[2]);

        switch (split[1]) {
        case "in":
            return arr.indexOf(value) != -1;
        }

        return false;
    }

    private _parseCommand(object: AS.ASObject, command: string, renderResult: RenderResult, reg: Registers): string|{template: string}
    {
        let result: any = null;
        let isHtml = false;
        let depend: string = null;
        if (command.indexOf('%') != -1) {
            let nodepend = command.split(' %', 1)[0];
            depend = "$" + command.substring(command.indexOf('%') + 1);
            command = nodepend;
        }
        for (let asf of command.split(' ')) {
            if (asf.startsWith("$"))
            {
                if (result !== null) continue;
                const name = asf.substring(1);
                let results = [];
                for (let item of AS.get(object, name)) {
                    if ((typeof item) == "object" && !Array.isArray(item)) results.push(JSON.stringify(item));
                    else results.push(item);
                }

                if (results.length == 0) result = null;
                else result = results;
            } else if (asf.startsWith("'")) {
                if (result === null) result = asf.substring(1);
            } else if (asf == "ishtml") {
                isHtml = true;
            } else if (asf.startsWith("render:")) {
                const template = asf.substring(7);
                if (result == null) {
                    return {template};
                }
                let id: string = null;
                if (Array.isArray(result))
                    id = result[0] as string;
                else id = result as string;

                renderResult.subRender.push({id, template});
                return null;
            } else if (asf == "load") {
                reg.load = result as string[];
                reg.accumulator = -1;
                result = null;
            } else if (asf == "item") {
                if (reg.accumulator < reg.load.length && reg.accumulator >= 0 && result == null) result = reg.load[reg.accumulator];
            } else if (asf.startsWith("client.")) {
                if (asf == "client.stats") result = `Preloaded ${Object.keys((window as any).preload).length} items`;
            }
        }

        if (depend != null) {
            if (Array.isArray(result)) result = result[0];
            renderResult.subRender.push({template: JSON.stringify({command: depend, wrap: "span"}), id: result});
            return null;
        }

        if (result == null) return "";

        let text: string;
        if (Array.isArray(result)) text = result[0].toString();
        else text = result == null ? "" : result.toString();

        if (!isHtml) text = text.replace(/</g, "&lt;").replace(/>/g, "&gt;");;

        return text;
    }
    
    public render(template: string, object: AS.ASObject, renderResult?: RenderResult): RenderResult {
        if (renderResult == null) renderResult = new RenderResult();
        if (template.startsWith('{')) {
            // pseudo-template!
            let data = JSON.parse(template);
            let parsed = this._parseCommand(object, data.command, renderResult, new Registers());
            renderResult.result.push(parsed as string);
            return renderResult;
        }

        let temp = this.templates[template];
        let result = "";
        let reg = new Registers();
        for (let i = 0; i < temp.length; i++) {
            const item = temp[i];
            switch (item.type) {
                case "text":
                    result += item.data;
                    break;
                case "if":
                case "while":
                    if (!this._parseCondition(object, item.data.substring(item.data.indexOf(' ') + 1), reg)) i = item.offset - 1;
                    break;
                case "jump":
                    i = item.offset - 1;
                    break;
                case "end":
                    if (temp[item.offset].type == "while")
                        i = item.offset - 1;
                    break;
                case "command":
                    let parsed = this._parseCommand(object, item.data, renderResult, reg);
                    if (parsed != null && typeof parsed == "object") {
                        let template = (parsed as {template: string}).template;
                        let offset = renderResult.result.length;
                        result += `<${this.templates[template][0].data}>`;
                        this.render(template, object, renderResult);
                        renderResult.result[offset] = result + renderResult.result[offset];
                        result = renderResult.result[renderResult.result.length - 1] + `</${this.templates[template][0].data.split(' ')[0]}>`;
                        renderResult.result.splice(renderResult.result.length - 1);
                    } else if (parsed == null) {
                        renderResult.result.push(result);
                        result = "";
                    } else {
                        result += parsed;
                    }

                    break;
            }
        }
        renderResult.result.push(result);
        return renderResult;
    }
}