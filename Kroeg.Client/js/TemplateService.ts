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
    public result: string[] = [];
    public subRender: {id: string, template: string}[] = [];
}

export class TemplateRenderer {
    private templates: {[item: string]: Template};

    public constructor(private templateService: TemplateService, private entityStore: EntityStore) {
    }

    public async prepare() {
        this.templates = await this.templateService.getTemplates();
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

    private _parseCommand(object: AS.ASObject, command: string, renderResult: RenderResult): string
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
                    renderResult.subRender.push({template, id: object.id});
                    return null;
                }
                let id: string = null;
                if (Array.isArray(result))
                    id = result[0] as string;
                else id = result as string;

                renderResult.subRender.push({id, template});
                return null;
            }
        }

        if (depend != null) {
            if (Array.isArray(result)) result = result[0];
            console.log(JSON.stringify(command), "||", depend, "||", result);
            renderResult.subRender.push({template: JSON.stringify({command: depend}), id: result});
            return null;
        }

        let text: string;
        if (Array.isArray(result)) text = result[0].toString();
        else text = result == null ? "" : result.toString();

        if (!isHtml) text = text.replace(/</g, "&lt;").replace(/>/g, "&gt;");;

        return text;
    }
    
    public async render(template: string, object: AS.ASObject): Promise<RenderResult> {
        let renderResult = new RenderResult();
        if (template.startsWith('{')) {
            // pseudo-template!
            let data = JSON.parse(template);
            let parsed = this._parseCommand(object, data.command, renderResult);
            renderResult.result.push(parsed);
            return renderResult;
        }

        let temp = this.templates[template];
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
                    let parsed = this._parseCommand(object, item.data, renderResult);
                    if (parsed == null) {
                        renderResult.result.push(result);
                        result = "";
                    }
                    break;
            }
        }
        renderResult.result.push(result);
        return renderResult;
    }
}