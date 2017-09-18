import { Session } from "./Session";
import { TemplateService, TemplateRenderer } from "./TemplateService";
import { EntityStore } from "./EntityStore";
import { RenderHost } from "./RenderHost";

export async function setup() {
    let session = new Session();
    await session.set("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmMWU0OTYzMi1iYWRhLTQyMWYtYjY3Ny1iMmI1ZTU1ZGE3MTAiLCJhY3RvciI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMC91c2Vycy9wdWNraXBlZGlhIiwibmJmIjoxNTA1NDAzNDA2LCJleHAiOjE1MDc5OTU0MDYsImlzcyI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMC8iLCJhdWQiOiJodHRwOi8vbG9jYWxob3N0OjUwMDAvIn0.253Y2hyR9mMoeNETLvDhNmtoUaBFZ6lVJGgrleHPWzQ", "http://localhost:5000/users/puckipedia");

    let entityStore = new EntityStore(session);
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
