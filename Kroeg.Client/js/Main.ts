import { Session } from "./Session";
import { TemplateService, TemplateRenderer } from "./TemplateService";
import { EntityStore } from "./EntityStore";
import { RenderHost } from "./RenderHost";

if (window.location.hash.length > 0) {
    let split = window.location.hash.substring(1).split('&');
    let kvp: {[a: string]: string} = {};
    for (let item of split) {
        let splitItem = item.split('=');
        kvp[splitItem[0]] = decodeURIComponent(splitItem[1]);
    }
    //[oauth2]&response_type=token&redirect_uri=http://localhost:5000/asdf/&state=asdf
    if ("access_token" in kvp && "state" in kvp && "expires_in" in kvp) {
        window.localStorage.setItem("id", kvp.state);
        window.localStorage.setItem("access_token", kvp.access_token);
        window.localStorage.setItem("expires", (+(new Date) + parseInt(kvp.expires_in)).toString());
    }
    window.history.replaceState({}, document.title, window.location.toString().replace(window.location.hash, ""));
}

let session: Session = null;
let entityStore: EntityStore;

async function login() {
    let id = prompt("Log in as?", window.location.toString());
    let data = await entityStore.get(id, false);
    let endpoints = await entityStore.get(data["endpoints"]);
    let oauthEndpoint = endpoints["oauthAuthorizationEndpoint"] as string;
    if (oauthEndpoint.indexOf("?") == -1) oauthEndpoint += "?"; else oauthEndpoint += "&";
    oauthEndpoint += `state=${encodeURIComponent(id)}&response_type=token&redirect_uri=${window.location.toString()}`;
    window.location.assign(oauthEndpoint);
}

(window as any).login = login;

export async function setup() {
    session = new Session();
    if (window.localStorage.getItem("access_token") == null)
        await session.set(null, null);
    else
        await session.set(window.localStorage["access_token"], window.localStorage["id"]);

    entityStore = new EntityStore(session);
    let renderer = new TemplateRenderer(new TemplateService(), entityStore);
    await renderer.prepare();
    let container = document.getElementsByClassName("container")[0] as HTMLDivElement;
    let renderHost: RenderHost = new RenderHost(renderer, entityStore, window.location.toString().replace("?nopreload", ""), "body", container);
    let update = (id: string) => {
        renderHost.id = id;
        console.log("Update: ", id);
    };

    let sub = (e: MouseEvent, target: HTMLAnchorElement): boolean => {
        if (target.tagName == "A" && target.hostname == location.hostname) {
            e.stopPropagation();
            e.preventDefault();
            window.history.pushState({id: target.href}, document.title, target.href);
            update(target.href);
            return false;
        } else {
            if (target.parentElement != null) return sub(e, target.parentElement as HTMLAnchorElement);
        }
    }

    document.addEventListener("click", (e) => sub(e, e.target as HTMLAnchorElement), true);

    window.addEventListener("popstate", (e) => {
        update(window.location.toString());
    }, false);
}

setup();
