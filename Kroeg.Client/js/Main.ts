import { Session } from "./Session";
import { TemplateService, TemplateRenderer } from "./TemplateService";
import { EntityStore } from "./EntityStore";
import { RenderHost } from "./RenderHost";

export async function things() {
    let session = new Session();
    await session.set("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmMWU0OTYzMi1iYWRhLTQyMWYtYjY3Ny1iMmI1ZTU1ZGE3MTAiLCJhY3RvciI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMC91c2Vycy9wdWNraXBlZGlhIiwibmJmIjoxNTA1NDAzNDA2LCJleHAiOjE1MDc5OTU0MDYsImlzcyI6Imh0dHA6Ly9sb2NhbGhvc3Q6NTAwMC8iLCJhdWQiOiJodHRwOi8vbG9jYWxob3N0OjUwMDAvIn0.253Y2hyR9mMoeNETLvDhNmtoUaBFZ6lVJGgrleHPWzQ", "http://localhost:5000/users/puckipedia");
    let entityStore = new EntityStore(session);
    let renderer = new TemplateRenderer(new TemplateService(), entityStore);
    let renderHost = new RenderHost(renderer, entityStore, "http://localhost:5000/users/puckipedia", "object");
    document.body.appendChild(renderHost.element);
}

things();
