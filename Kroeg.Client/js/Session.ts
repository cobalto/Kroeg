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

    public async set(token: string, user: string): Promise<void> {
        this._token = token;
        this._user = user;
        this._host = getHost(this._user);

        let userData = await (await this.authFetch(user)).json();
        if ("endpoints" in userData) {
            const endpoints = userData["endpoints"];
            if ("proxyUrl" in endpoints) this._proxyUrl = endpoints["proxyUrl"];
            if ("uploadMedia" in endpoints) this._uploadMedia = endpoints["uploadMedia"];
        }
    }

    public authFetch(input: string | Request, init?: RequestInit): Promise<Response> {
        let request = new Request(input, init);
        request.headers.set("Authorization", this._token);
        request.headers.set("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json");
        return fetch(request);
    }

    public getObject(url: string): Promise<any> {
        const requestHost = getHost(url);
        if (requestHost != this._host && this._proxyUrl !== undefined) {
            const parms = new URLSearchParams();
            parms.append("id", url);
            let requestInit: RequestInit = {
                body: parms
            };

            return this.authFetch(this._proxyUrl, requestInit);
        }
    }
}