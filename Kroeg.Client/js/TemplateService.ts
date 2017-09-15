import { EntityStore } from "./EntityStore";
import * as AS from "./AS";

export class TemplateItem {
    public type: "command" | "if" | "while" | "else" | "jump" | "end" | "text";
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
    public result: string;
    public usedIds: string[] = [];
}

export class TemplateRenderer {
    public constructor(private templateService: TemplateService, private entityStore: EntityStore) {

    }

    private _parseCondition(object: AS.ASObject, text: string): boolean {
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

    private async _parseCommand(object: AS.ASObject, command: string, renderResult: RenderResult): Promise<string>
    {
        let result: any = null;
        let isHtml = false;
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
            }
            else if (asf.startsWith("%")) {
                if (result === null) continue;
                let id: string = null;
                if (Array.isArray(result))
                    id = result[0] as string;
                else id = result as string;

                const entity = await this.entityStore.get(id);
                const name = asf.substring(1);
                let results = [];
                for (let item of AS.get(entity, name)) {
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
                if (result == null) return await this._render(template, object.id, renderResult);
                let id: string = null;
                if (Array.isArray(result))
                    id = result[0] as string;
                else id = result as string;

                return await this._render(template, id, renderResult);
            }
        }

        let text: string;
        if (Array.isArray(result)) text = result[0].toString();
        else text = result == null ? "" : result.toString();

        if (isHtml) return text;
        return text.replace(/</g, "&lt;").replace(/>/g, "&gt;");
    }
    
    private async _render(template: string, mainId: string, renderResult: RenderResult): Promise<string> {
        if (renderResult == null) renderResult = new RenderResult();

        renderResult.usedIds.push(mainId);

        const object = await this.entityStore.get(mainId);
        let temp = (await this.templateService.getTemplates())[template];
        let result = "";
        for (let i = 0; i < temp.length; i++) {
            const item = temp[i];
            switch (item.type) {
                case "text":
                    result += item.data;
                    break;
                case "if":
                case "while":
                    if (!this._parseCondition(object, item.data.substring(item.data.indexOf(' ') + 1))) i = item.offset - 1;
                    break;
                case "jump":
                    i = item.offset - 1;
                    break;
                case "end":
                    if (temp[item.offset].type == "while")
                        i = item.offset - 1;
                    break;
                case "command":
                    result += await this._parseCommand(object, item.data, renderResult);
                    break;
            }
        }
        return result;
    }

    public async render(template: string, mainId: string): Promise<RenderResult>
    {
        let renderResult = new RenderResult();
        renderResult.result = await this._render(template, mainId, renderResult);

        return renderResult;
    }
}