var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : new P(function (resolve) { resolve(result.value); }).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
define("AS", ["require", "exports"], function (require, exports) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    function get(object, name) {
        if (object == null)
            debugger;
        if (name in object) {
            if (Array.isArray(object[name]))
                return object[name];
            return [object[name]];
        }
        return [];
    }
    exports.get = get;
    function contains(object, name, value) {
        return get(object, name).indexOf(value) != -1;
    }
    exports.contains = contains;
    function containsAny(object, name, values) {
        const data = get(object, name);
        for (let value of values)
            if (data.indexOf(values) != -1)
                return true;
        return false;
    }
    exports.containsAny = containsAny;
    function set(object, name, value) {
        if (name in object) {
            if (Array.isArray(object[name]))
                object[name].push(value);
            object[name] = [object[name], value];
        }
        object[name] = value;
    }
    exports.set = set;
    function clear(object, name) {
        if (name in object)
            delete object[name];
    }
    exports.clear = clear;
    class ASObject {
    }
    exports.ASObject = ASObject;
});
define("Session", ["require", "exports"], function (require, exports) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    function getHost(url) {
        const obj = document.createElement('a');
        // Let the browser do the work
        obj.href = url;
        return obj.protocol + "://" + obj.hostname;
    }
    class Session {
        constructor() {
        }
        set(token, user) {
            return __awaiter(this, void 0, void 0, function* () {
                this._token = token;
                this._user = user;
                this._host = getHost(this._user);
                let userData = yield (yield this.authFetch(user)).json();
                if ("endpoints" in userData) {
                    const endpoints = userData["endpoints"];
                    if ("proxyUrl" in endpoints)
                        this._proxyUrl = endpoints["proxyUrl"];
                    if ("uploadMedia" in endpoints)
                        this._uploadMedia = endpoints["uploadMedia"];
                }
            });
        }
        authFetch(input, init) {
            let request = new Request(input, init);
            request.headers.set("Authorization", this._token);
            request.headers.set("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json");
            return fetch(request);
        }
        getObject(url) {
            return __awaiter(this, void 0, void 0, function* () {
                const requestHost = getHost(url);
                if (requestHost != this._host && this._proxyUrl !== undefined) {
                    const parms = new URLSearchParams();
                    parms.append("id", url);
                    let requestInit = {
                        body: parms
                    };
                    return yield (yield this.authFetch(this._proxyUrl, requestInit)).json();
                }
                return yield (yield this.authFetch(url)).json();
            });
        }
    }
    exports.Session = Session;
});
define("EntityStore", ["require", "exports", "jsonld"], function (require, exports, jsonld) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    class StoreActivityToken {
        constructor() {
            this.items = {};
        }
        addToHandler(id, handler) {
            if (!(id in this.items))
                this.items[id] = [];
            this.items[id].push(handler);
        }
    }
    exports.StoreActivityToken = StoreActivityToken;
    class EntityStore {
        constructor(session) {
            this.session = session;
            this._handlers = {};
            this._cache = {};
        }
        _addToHandler(id, handler) {
            if (!(id in this._handlers))
                this._handlers[id] = [];
            this._handlers[id].push(handler);
        }
        _removeFromHandler(id, handler) {
            this._handlers[id].splice(this._handlers[id].indexOf(handler), 1);
        }
        register(handlers, existing) {
            if (existing == null)
                existing = new StoreActivityToken();
            for (let id in handlers) {
                this._addToHandler(id, handlers[id]);
                existing.addToHandler(id, handlers[id]);
            }
            return existing;
        }
        deregister(handler) {
            for (let id in handler.items) {
                for (let item of handler.items[id])
                    this._removeFromHandler(id, item);
            }
            handler.items = {};
        }
        _addToCache(id, obj) {
            let prev = undefined;
            if (id in this._cache)
                prev = this._cache[id];
            this._cache[id] = obj;
            if (id in this._handlers)
                for (let handler of this._handlers[id])
                    handler(prev, obj);
        }
        loadDocument(url, callback) {
            return __awaiter(this, void 0, void 0, function* () {
                try {
                    let response = yield this.session.authFetch(url);
                    let data = yield response.json();
                    callback(null, data);
                }
                catch (e) {
                    callback(e, null);
                }
            });
        }
        get(id, cache = true) {
            return __awaiter(this, void 0, void 0, function* () {
                if (id in this._cache && cache)
                    return this._cache[id];
                let processor = new jsonld.JsonLdProcessor();
                let data = yield this.session.getObject(id);
                let flattened = yield processor.flatten(data, data);
                for (let item of flattened["@graph"]) {
                    this._addToCache(item["id"], item);
                }
                return this._cache[id];
            });
        }
    }
    exports.EntityStore = EntityStore;
});
define("TemplateService", ["require", "exports", "AS"], function (require, exports, AS) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    class TemplateItem {
    }
    exports.TemplateItem = TemplateItem;
    class TemplateService {
        constructor() {
            this._templatePromise = this._getTemplates();
        }
        _getTemplates() {
            return __awaiter(this, void 0, void 0, function* () {
                const result = yield fetch("http://localhost:5000/settings/templates");
                return yield result.json();
            });
        }
        getTemplates() {
            return this._templatePromise;
        }
    }
    exports.TemplateService = TemplateService;
    class RenderResult {
        constructor() {
            this.result = [];
            this.subRender = [];
        }
    }
    exports.RenderResult = RenderResult;
    class TemplateRenderer {
        constructor(templateService, entityStore) {
            this.templateService = templateService;
            this.entityStore = entityStore;
        }
        prepare() {
            return __awaiter(this, void 0, void 0, function* () {
                this.templates = yield this.templateService.getTemplates();
            });
        }
        _parseCondition(object, text) {
            const split = text.split(' ');
            if (split.length == 2) {
                if (text == "is Activity")
                    return "actor" in object;
                else if (text == "is Collection")
                    return AS.containsAny(object, "type", ["Collection", "OrderedCollection"]);
                else if (text == "is CollectionPage")
                    return AS.containsAny(object, "type", ["CollectionPage", "OrderedCollectionPage"]);
                else if (split[0] == "is")
                    return AS.contains(object, "type", split[1]);
                else if (split[0] == "has")
                    return AS.get(object, split[1]).length > 0;
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
        _parseCommand(object, command, renderResult) {
            let result = null;
            let isHtml = false;
            let depend = null;
            if (command.indexOf('%') != -1) {
                let nodepend = command.split(' %', 1)[0];
                depend = "$" + command.substring(command.indexOf('%') + 1);
                command = nodepend;
            }
            for (let asf of command.split(' ')) {
                if (asf.startsWith("$")) {
                    if (result !== null)
                        continue;
                    const name = asf.substring(1);
                    let results = [];
                    for (let item of AS.get(object, name)) {
                        if ((typeof item) == "object" && !Array.isArray(item))
                            results.push(JSON.stringify(item));
                        else
                            results.push(item);
                    }
                    if (results.length == 0)
                        result = null;
                    else
                        result = results;
                }
                else if (asf.startsWith("'")) {
                    if (result === null)
                        result = asf.substring(1);
                }
                else if (asf == "ishtml") {
                    isHtml = true;
                }
                else if (asf.startsWith("render:")) {
                    const template = asf.substring(7);
                    if (result == null) {
                        renderResult.subRender.push({ template, id: object.id });
                        return null;
                    }
                    let id = null;
                    if (Array.isArray(result))
                        id = result[0];
                    else
                        id = result;
                    renderResult.subRender.push({ id, template });
                    return null;
                }
            }
            if (depend != null) {
                if (Array.isArray(result))
                    result = result[0];
                console.log(JSON.stringify(command), "||", depend, "||", result);
                renderResult.subRender.push({ template: JSON.stringify({ command: depend }), id: result });
                return null;
            }
            let text;
            if (Array.isArray(result))
                text = result[0].toString();
            else
                text = result == null ? "" : result.toString();
            if (!isHtml)
                text = text.replace(/</g, "&lt;").replace(/>/g, "&gt;");
            ;
            return text;
        }
        render(template, object) {
            return __awaiter(this, void 0, void 0, function* () {
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
                            if (!this._parseCondition(object, item.data.substring(item.data.indexOf(' ') + 1)))
                                i = item.offset - 1;
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
            });
        }
    }
    exports.TemplateRenderer = TemplateRenderer;
});
define("RenderHost", ["require", "exports"], function (require, exports) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    class RenderHost {
        constructor(renderer, store, id, template, dom) {
            this.renderer = renderer;
            this.store = store;
            this._subrender = [];
            this._dom = dom != null ? dom : document.createElement("div");
            this._id = id;
            this._template = template;
            this.update();
        }
        get id() { return this._id; }
        set id(value) { this._id = value; this.update(); }
        get template() { return this._template; }
        set template(value) { this._template = value; this.render(); }
        update() {
            return __awaiter(this, void 0, void 0, function* () {
                if (this._storeActivityToken != null)
                    this.store.deregister(this._storeActivityToken);
                const handlers = {};
                handlers[this._id] = this._reupdate.bind(this);
                this._storeActivityToken = this.store.register(handlers);
                this._object = yield this.store.get(this._id);
                this.render();
            });
        }
        deregister() {
            if (this._storeActivityToken != null)
                this.store.deregister(this._storeActivityToken);
        }
        get element() { return this._dom; }
        _reupdate(old, newObject) {
            this._object = newObject;
            this.render();
        }
        render() {
            return __awaiter(this, void 0, void 0, function* () {
                if (this._object == null)
                    return;
                for (let subrender of this._subrender)
                    subrender.deregister();
                this._subrender.splice(0);
                const result = yield this.renderer.render(this._template, this._object);
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
                    let holder = document.getElementById(`_renderhost_holder_${counterStart + i}`);
                    let host = new RenderHost(this.renderer, this.store, subrender.id, subrender.template, holder);
                    this._subrender.push(host);
                }
                this._lastResult = result;
            });
        }
    }
    RenderHost._counter = 0;
    exports.RenderHost = RenderHost;
});
define("Main", ["require", "exports", "Session", "TemplateService", "EntityStore", "RenderHost"], function (require, exports, Session_1, TemplateService_1, EntityStore_1, RenderHost_1) {
    "use strict";
    Object.defineProperty(exports, "__esModule", { value: true });
    function things() {
        return __awaiter(this, void 0, void 0, function* () {
            let session = new Session_1.Session();
            yield session.set("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmMWU0OTYzMi1iYWRhLTQyMWYtYjY3Ny1iMmI1ZTU1ZGE3MTAiLCJhY3RvciI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMC91c2Vycy9wdWNraXBlZGlhIiwibmJmIjoxNTA1NDAzNDA2LCJleHAiOjE1MDc5OTU0MDYsImlzcyI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMC8iLCJhdWQiOiJodHRwOi8vbG9jYWxob3N0OjUwMDAvIn0.253Y2hyR9mMoeNETLvDhNmtoUaBFZ6lVJGgrleHPWzQ", "http://localhost:5000/users/puckipedia");
            let entityStore = new EntityStore_1.EntityStore(session);
            window.entityStore = entityStore;
            let renderer = new TemplateService_1.TemplateRenderer(new TemplateService_1.TemplateService(), entityStore);
            yield renderer.prepare();
            let renderHost = new RenderHost_1.RenderHost(renderer, entityStore, "http://localhost:5000/users/puckipedia", "object");
            document.body.appendChild(renderHost.element);
        });
    }
    exports.things = things;
    things();
});
//# sourceMappingURL=code.js.map