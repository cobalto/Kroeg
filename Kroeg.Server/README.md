# Kroeg.Server

## Description
This project contains most of the ActivityPub server. It starts up a simple pipeline, with MVC and static files. It also injects its own middleware, the [GetEntityMiddleware](Middleware/GetEntityMiddleware.cs).
This middleware tries to read an entity from the database, based on the `Host` header (and rewrites `http` to `https` if `RewriteRequestScheme` is set in the config), and either calls into MVC to run the [RenderController](Controllers/RenderController.cs), or directly returns AS2 JSON (or OStatus-compatible ActivityStreams Atom).

## Using it
To set up Kroeg and properly create an actor, set up with an `inbox`, `outbox`, and all the `Collection`s it should have, first enzsure you have the dependencies installed:

 - .NET Core 1.1
 - PostgreSQL

First restore the dependencies and build the project, then in `appsettings.json` fix up the settings to your preferences.

 - `TokenSigningKey`: This key is used to sign all the authentication tokens, so if it leaks out anyone can impersonate anyone else. Please don't leak it.
 - `BaseUri`: The URI under which all the `Object`s and `Activity`s are created. Ensure this path gets passed to this server, and has a trailing slash. It also should be `https`. `gopher` is not yet supported, sorry.
 - `RewriteRequestScheme`: If you use a load balancer or other system which does SSL termination, this server might try to read all IDs with `http`. In this case, set this to `true`, so the paths are properly resolved.

Then under `ConnectionStrings`, set the database connection parameters.

Now, start the server, and go to `/auth/login`. Here, enter any credentials you want. If no account exists with that username, one will be made (note: this server isn't really production-ready yet, so yeah).

Then, press new and enter the parameters for the Actor. then in the actor list, select it and choose `Get Token`. (note: this is all temporary, until I implement OAuth2...) This token can now be used in ActivityPub requests to the `outbox` as specified in the actor's data, as `Authorization: Bearer [...]`

## Notes on parts of the system
### Background tasks
Some tasks, like sending requests to WebSub targets or other ActivityPub servers, might not finish instantly. These are scheduled into the database, then ran in a background thread. This mechanism should work invisibly, but mean that not all requests are instant. I do not implement `publicInbox`.

### Entity store
All entities are requested via a simple interface: `IEntityStore`. All these entities should be flattened as far as possible (transient entities don't have to be, because those don't have IDs) with the `EntityFlattener`.
When data comes in from an external source, the server doesn't directly push it into the database, but instead puts it in a `StagingEntityStore`. This entity store is used for the processing the request, and once the data appears to be fine, we just commit the changes into the actual database. There is no purging mechanism yet, so cached entities stay forever (and never get re-requested).

### OStatus support
OStatus support is done by translating the Atom XML into ActivityStreams2 JSON-LD and back at the very first and last moments. `Undo`s of likes and retweets are slightly unstable still, and use PostgreSQL's `jsonb` to implement.

## TODOs
There's a ton left to do.

- [x] Implement OAuth2
- [ ] Properly do authorization and authentication
- [ ] Ensure people may view the objects. (filter on top of the entity store?)
- [ ] Proper JSON-LD instead of the current semi-implementation (work in progress, see [Kroeg.JsonLD](../Kroeg.JsonLD))
- [ ] Nice frontend
- [ ] Send salmons back to OStatus servers
- [ ] Allow ActivityPub people to follow OStatus people
- [ ] Twitter integration???
- [ ] Don't cache external entities forever