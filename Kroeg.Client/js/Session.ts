import { ASObject } from "./AS";

function getHost(url: string) {
    const obj = document.createElement('a');
    // Let the browser do the work
    obj.href = url;
    return obj.protocol + "://" + obj.hostname;
}


export class Session {
    constructor() {

    }

    private _token: string;
    private _user: string;
    private _host: string;

    private _proxyUrl: string;
    private _uploadMedia: string;
    private _outbox: string;

    public get user() { return this._user; }

    public async set(token: string, user: string): Promise<void> {
        this._token = token;
        this._user = user;
        if (token == null) return;
        this._host = getHost(this._user);

        let userData = await (await this.authFetch(user)).json();
        this._outbox = userData["outbox"];
        if ("endpoints" in userData) {
            const endpoints = userData["endpoints"];
            if ("proxyUrl" in endpoints) this._proxyUrl = endpoints["proxyUrl"];
            if ("uploadMedia" in endpoints) this._uploadMedia = endpoints["uploadMedia"];
        }
    }

    public authFetch(input: string | Request, init?: RequestInit): Promise<Response> {
        let request = new Request(input, init);
        if (this._token != null)
            request.headers.set("Authorization", "Bearer " + this._token);
        request.headers.set("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json");
        return fetch(request);
    }

    public async getObject(url: string): Promise<ASObject> {
        const requestHost = getHost(url);
        if (requestHost != this._host && this._proxyUrl !== undefined) {
            const parms = new URLSearchParams();
            parms.append("id", url);
            let requestInit: RequestInit = {
                body: parms
            };

            return await (await this.authFetch(this._proxyUrl, requestInit)).json();
        }

        return await (await this.authFetch(url)).json();
    }
}