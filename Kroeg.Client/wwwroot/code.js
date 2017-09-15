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
            console.log(`Cached ${id} - ${Object.keys(this._cache).length} items cached`);
        }
        loadDocument(url, callback) {
            return __awaiter(this, void 0, void 0, function* () {
                console.log("Loading ...", url);
                try {
                    let response = yield this.session.authFetch(url);
                    let data = yield response.json();
                    console.log("Callbacking");
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
            this.usedIds = [];
        }
    }
    exports.RenderResult = RenderResult;
    class TemplateRenderer {
        constructor(templateService, entityStore) {
            this.templateService = templateService;
            this.entityStore = entityStore;
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
            return __awaiter(this, void 0, void 0, function* () {
                let result = null;
                let isHtml = false;
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
                    else if (asf.startsWith("%")) {
                        if (result === null)
                            continue;
                        let id = null;
                        if (Array.isArray(result))
                            id = result[0];
                        else
                            id = result;
                        const entity = yield this.entityStore.get(id);
                        const name = asf.substring(1);
                        let results = [];
                        for (let item of AS.get(entity, name)) {
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
                        if (result == null)
                            return yield this._render(template, object.id, renderResult);
                        let id = null;
                        if (Array.isArray(result))
                            id = result[0];
                        else
                            id = result;
                        return yield this._render(template, id, renderResult);
                    }
                }
                let text;
                if (Array.isArray(result))
                    text = result[0].toString();
                else
                    text = result == null ? "" : result.toString();
                if (isHtml)
                    return text;
                return text.replace(/</g, "&lt;").replace(/>/g, "&gt;");
            });
        }
        _render(template, mainId, renderResult) {
            return __awaiter(this, void 0, void 0, function* () {
                if (renderResult == null)
                    renderResult = new RenderResult();
                renderResult.usedIds.push(mainId);
                const object = yield this.entityStore.get(mainId);
                let temp = (yield this.templateService.getTemplates())[template];
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
                            result += yield this._parseCommand(object, item.data, renderResult);
                            break;
                    }
                }
                return result;
            });
        }
        render(template, mainId) {
            return __awaiter(this, void 0, void 0, function* () {
                let renderResult = new RenderResult();
                renderResult.result = yield this._render(template, mainId, renderResult);
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
        constructor(renderer, store, id, template) {
            this.renderer = renderer;
            this.store = store;
            this._dom = document.createElement("div");
            this._id = id;
            this._template = template;
            this.render();
        }
        get id() { return this._id; }
        set id(value) { this._id = value; this.render(); }
        get template() { return this._template; }
        set template(value) { this._template = value; this.render(); }
        get element() { return this._dom; }
        _reupdate(old, newObject) {
            this.render();
        }
        render() {
            return __awaiter(this, void 0, void 0, function* () {
                const result = yield this.renderer.render(this._template, this._id);
                this._dom.innerHTML = result.result;
                if (this._storeActivityToken != null)
                    this.store.deregister(this._storeActivityToken);
                const handlers = {};
                for (let id of result.usedIds)
                    handlers[id] = this._reupdate;
                this._storeActivityToken = this.store.register(handlers);
                this._lastResult = result;
            });
        }
    }
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
            let renderer = new TemplateService_1.TemplateRenderer(new TemplateService_1.TemplateService(), entityStore);
            let renderHost = new RenderHost_1.RenderHost(renderer, entityStore, "http://localhost:5000/users/puckipedia", "object");
            document.body.appendChild(renderHost.element);
        });
    }
    exports.things = things;
    things();
});
//# sourceMappingURL=code.js.map