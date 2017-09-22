import { EntityStore } from "./EntityStore";
import { Session } from "./Session";

export class SessionObjects {
    public get navbar(): string { return "kroeg:navbar" }

    constructor(private _store: EntityStore, private _session: Session) {
        this.regenerate();
    }

    public regenerate() {
        let navbar: {id: string, loggedInAs?: string} = {
            id: "kroeg:navbar"
        };

        if (this._session.user != null) navbar.loggedInAs = this._session.user;

        this._store.internal("navbar", navbar);
    }
}