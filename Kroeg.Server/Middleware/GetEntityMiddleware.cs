using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Middleware.Handlers;
using Kroeg.Server.Middleware.Handlers.ClientToServer;
using Kroeg.Server.Middleware.Handlers.ServerToServer;
using Kroeg.Server.Middleware.Handlers.Shared;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Salmon;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.Extensions.DependencyInjection;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Middleware
{
    public class GetEntityMiddleware
    {
        private readonly RequestDelegate _next;

        public GetEntityMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        private readonly List<string> _accepts = new List<string>
        {
            "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"",
            "application/activity+json",
            "application/atom+xml",
            "text/html"
        };

        public async Task Invoke(HttpContext context, APContext acontext, EntityFlattener flattener, DeliveryService audienceHelper, CollectionTools collectionTools, IEntityStore mainStore, AtomEntryParser entryParser, AtomEntryGenerator entryGenerator, IServiceProvider serviceProvider, EntityData entityData)
        {
            var handler = new GetEntityHandler(acontext, flattener, mainStore,
                entryGenerator, serviceProvider, entityData);
            if (entityData.RewriteRequestScheme) context.Request.Scheme = "https";

            var fullpath = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";
            if (fullpath.EndsWith(".atom"))
            {
                context.Request.Headers.Remove("Accept");
                context.Request.Headers.Add("Accept", "application/atom+xml");
                fullpath = fullpath.Remove(fullpath.Length - 5);
            }

            if (context.Request.QueryString.Value == "?hub")
            {
                context.Items.Add("fullPath", fullpath);
                context.Request.Path = "/.well-known/hub";
                context.Request.QueryString = QueryString.Empty;
            }

            var fromId = context.Request.Query.ContainsKey("from_id") ? (int?)int.Parse(context.Request.Query["from_id"]) : null;

            if (context.Request.Headers["Accept"].Contains("text/event-stream"))
            {
                await handler._handleEventStream(context);
                return;
            }
            if (context.Request.Method == "HEAD")
            {
                if (await mainStore.GetEntity(fullpath, false) != null)
                {
                    var allAccepts = context.Request.Headers["Accept"].SelectMany(a => a.Split(new [] { ", " }, StringSplitOptions.RemoveEmptyEntries)).ToList();
                    var firstAccept = allAccepts.FirstOrDefault(_accepts.Contains);
                    if (firstAccept != null)
                        context.Response.ContentType = firstAccept;
                    if (allAccepts.Contains("*/*") || allAccepts.Contains("text/*"))
                        context.Response.ContentType = "text/html";

                    context.Response.StatusCode = 200;
                    await context.Response.WriteAsync("");
                    return;
                }
            }

            if (context.Request.Method == "GET")
            {
                var data = await handler._get(fullpath, fromId);
                if (data != null)
                {
                    await handler._render(context, data, _next);
                    return;
                }
            }

            /* context.Request.ContentType == "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"" ||  */

            else if (context.Request.Method == "POST" && (context.Request.ContentType.Contains("application/ld+json") || (context.Request.QueryString.Value == "?salmon")))
            {
                var entity = await acontext.Entities.FirstOrDefaultAsync(a => a.Id == fullpath);
                if (entity == null)
                {
                    await _next(context);
                    return;
                }

                string jdata;
                using (var reader = new StreamReader(context.Request.Body))
                {
                    jdata = await reader.ReadToEndAsync();
                }
                ASObject obj;
                if (context.Request.QueryString.Value == "?salmon")
                {
                    var magicEnvelope = new MagicEnvelope(XDocument.Parse(jdata));
                    var rawData = magicEnvelope.RawData;
                    Console.WriteLine(rawData);
                    obj = await entryParser.Parse(XDocument.Parse(rawData));
                    var tmp = new StagingEntityStore(mainStore);
                    var flat = await flattener.FlattenAndStore(tmp, obj);
                    var asfd = await entryGenerator.Build(flat.Data, tmp);
                    Console.WriteLine(asfd.ToString());
                    if (obj == null) return; // ignore
                }
                else
                {
                    obj = ASObject.Parse(jdata);
                }
                var data = await handler._post(context, entity, obj);
                await handler._renderAsJson(context, data);
            }

            if (!context.Response.HasStarted)
            {
                await _next(context);
            }
        }
        private class GetEntityHandler
        {
            private readonly APContext _context;
            private readonly EntityFlattener _flattener;
            private readonly IEntityStore _mainStore;
            private readonly AtomEntryGenerator _entryGenerator;
            private readonly IServiceProvider _serviceProvider;
            private readonly EntityData _entityData;

            public GetEntityHandler(APContext acontext, EntityFlattener flattener, IEntityStore mainStore,
                AtomEntryGenerator entryGenerator, IServiceProvider serviceProvider,
                EntityData entityData)
            {
                _context = acontext;
                _flattener = flattener;
                _mainStore = mainStore;
                _entryGenerator = entryGenerator;
                _serviceProvider = serviceProvider;
                _entityData = entityData;
            }

            internal async Task _handleEventStream(HttpContext context)
            {
                var fullpath = $"{context.Request.Scheme}://{context.Request.Host.ToString().Replace("localhost", "home.empw.nl")}{context.Request.Path.ToString()}";
                var entity = await _mainStore.GetEntity(fullpath, false);

                if (entity.Type != "_inbox")
                {

                }
            }

            internal async Task _renderAsJson(HttpContext context, ASObject data)
            {
                var formatted = context.Request.Query.ContainsKey("formatted");
                var flatten = context.Request.Query.ContainsKey("flat");

                if (context.Request.Method == "GET")
                {
                    context.Response.StatusCode = data["type"].Contains(new ASTerm("Tombstone")) ? 410 :  200;
                }
                else
                {
                    context.Response.StatusCode = 201;
                    if (data != null)
                        context.Response.Headers.Add("Location", (string)data["id"].First().Primitive);
                }

                if (data == null)
                {
                    await context.Response.WriteAsync("\n");
                    return;
                }

                var entity = APEntity.From(data);

                context.Response.ContentType = "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"";

                if (flatten)
                {
                    var result = new Dictionary<string, JObject>();
                    var items = new Dictionary<string, APEntity>();
                    await _flattener.Unflatten(_mainStore, entity, mapped: items);

                    foreach (var item in items)
                        result[item.Key] = item.Value.Data.Serialize();

                    await context.Response.WriteAsync(JObject.FromObject(result).ToString(formatted ? Formatting.Indented : Formatting.None));
                }
                else
                    await context.Response.WriteAsync((await _flattener.Unflatten(_mainStore, entity)).Serialize().ToString(formatted ? Formatting.Indented : Formatting.None));
            }

            internal async Task _render(HttpContext context, ASObject @object, RequestDelegate next)
            {
                if (
                    (context.Request.Method == "POST" && (context.Request.ContentType == "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"" || context.Request.ContentType == "application/ld+json"))
                    || (context.Request.Method == "GET" && context.Request.Headers["Accept"].Any(a => a.Contains("application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\""))))
                {
                    await _renderAsJson(context, @object);
                }
                else if (context.Request.Headers["Accept"].Any(a => a == "application/atom+xml"))
                {
                    var parsed = (await _entryGenerator.Build(@object, _mainStore)).ToString();
                    context.Response.ContentType = "application/atom+xml";
                    var user = (string) @object["attributedTo"].Concat(@object["actor"]).First().Primitive;

                    var links = new List<string>
                    {
                        $"<{user}?hub>; rel=\"hub\"",
                        $"<{context.Request.Path}{context.Request.QueryString}>; rel=\"self\""
                    };

                    context.Response.Headers.Add("Link", string.Join(", ", links));

                    await context.Response.WriteAsync("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + parsed);
                }
                else
                {
                    context.Items["object"] = APEntity.From(@object);
                    context.Request.Path = new PathString("/render");
                    await next(context);
                }
            }

            internal async Task<ASObject> _post(HttpContext context, APEntity original, ASObject @object)
            {
                switch (original.Type)
                {
                    case "_inbox":
                        return await _serverToServer(context, original, @object);
                    case "_outbox":
                        var userId = original.Data["attributedTo"].FirstOrDefault() ?? original.Data["actor"].FirstOrDefault();
                        if (userId == null || context.User.FindFirst(JwtTokenSettings.ActorClaim).Value ==
                            (string) userId.Primitive) return await _clientToServer(context, original, @object);
                        context.Response.StatusCode = 403;
                        return null;
                }

                return null;
            }

            private readonly List<Type> _serverToServerHandlers = new List<Type>
            {
                typeof(VerifyOwnershipHandler),
                typeof(DeleteHandler),
                // likes, follows, announces, and undos change collections. Ownership has been verified, so it's prooobably safe to commit changes into the database.
                typeof(CommitChangesHandler),
                typeof(LikeFollowAnnounceHandler),
                typeof(UndoHandler),
                typeof(DeliveryHandler)
            };

            private async Task<ASObject> _serverToServer(HttpContext context, APEntity inbox, ASObject activity)
            {
                var stagingStore = new StagingEntityStore(_mainStore);
                var userId = (string) inbox.Data["attributedTo"].Single().Primitive;
                var user = await _mainStore.GetEntity(userId, false);

                //protected BaseHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox)

                var flattened = await _flattener.FlattenAndStore(stagingStore, activity);

                try
                {
                    using (var transaction = _context.Database.BeginTransaction())
                    {
                        foreach (var type in _clientToServerHandlers)
                        {
                            var handler = (BaseHandler)ActivatorUtilities.CreateInstance(_serviceProvider, type,
                                stagingStore, flattened, user, inbox);
                            var handled = await handler.Handle();
                            flattened = handler.MainObject;
                            if (!handled) break;
                        }

                        await _context.SaveChangesAsync();

                        transaction.Commit();

                        return flattened.Data;
                    }
                }
                catch (InvalidOperationException e)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync(e.Message);
                    return null;
                }
            }

            private readonly List<Type> _clientToServerHandlers = new List<Type>
            {
                typeof(ObjectWrapperHandler),
                typeof(ActivityMissingFieldsHandler),
                typeof(CreateActivityHandler),

                // commit changes before modifying collections
                typeof(CommitChangesHandler),
                typeof(FollowLikeHandler),
                typeof(AddRemoveActivityHandler),
                typeof(UndoActivityHandler),
                typeof(UpdateDeleteActivityHandler),
                typeof(DeliveryHandler)
            };

            private async Task<ASObject> _clientToServer(HttpContext context, APEntity outbox, ASObject activity)
            {
                var stagingStore = new StagingEntityStore(_mainStore);
                var userId = (string) outbox.Data["attributedTo"].Single().Primitive;
                var user = await _mainStore.GetEntity(userId, false);

                var activityType = (string) activity["type"].First().Primitive;
                if (activityType == "Create" || !_entityData.IsActivity(activityType))
                {
                    activity["id"].Clear();
                    if (activity["object"].SingleOrDefault()?.SubObject != null)
                        activity["object"].Single().SubObject["id"].Clear();
                }

                var flattened = await _flattener.FlattenAndStore(stagingStore, activity);

                try
                {
                    using (var transaction = _context.Database.BeginTransaction())
                    {
                        foreach (var type in _clientToServerHandlers)
                        {
                            var handler = (BaseHandler) ActivatorUtilities.CreateInstance(_serviceProvider, type,
                                stagingStore, flattened, user, outbox);
                            var handled = await handler.Handle();
                            flattened = handler.MainObject;
                            if (!handled) break;
                        }

                        await _context.SaveChangesAsync();

                        transaction.Commit();

                        return flattened.Data;
                    }
                }
                catch (InvalidOperationException e)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync(e.Message);
                    return null;
                }
            }

            internal async Task<ASObject> _get(string url, int? fromId)
            {
                var store = _mainStore;
                if (store is RetrievingEntityStore)
                    store = ((RetrievingEntityStore)store).Next;

                var entity = await store.GetEntity(url, true);
                if (entity == null) return null;
                if (entity.Type == "OrderedCollection" || entity.Type.StartsWith("_")) return await _getCollection(entity, fromId);

                return entity.Data;
            }

            private async Task<ASObject> _getCollection(APEntity entity, int? fromId)
            {
                var collection = entity.Data;
                collection["current"].Add(new ASTerm(entity.Id));
                collection["totalItems"].Add(new ASTerm(await _context.CollectionItems.CountAsync(a => a.CollectionId == entity.Id)));

                if (fromId != null)
                {
                    var items = await _context.CollectionItems.Where(a => a.CollectionItemId < fromId.Value && a.CollectionId == entity.Id).OrderByDescending(a => a.CollectionItemId).Take(10).ToListAsync();
                    var hasItems = items.Any();
                    var page = new ASObject();
                    page["type"].Add(new ASTerm("OrderedCollectionPage"));
                    page["summary"].Add(new ASTerm("A collection"));
                    page["id"].Add(new ASTerm(entity.Id + "?from_id=" + (hasItems ? fromId.Value : 0)));
                    page["partOf"].Add(new ASTerm(collection));
                    if (collection["attributedTo"].Any())
                        page["attributedTo"].Add(collection["attributedTo"].First());
                    if (items.Count > 0)
                        page["next"].Add(new ASTerm(entity.Id + "?from_id=" + items.Last().CollectionItemId));
                    page["orderedItems"].AddRange(items.Select(a => new ASTerm(a.ElementId)));

                    return page;
                }
                else
                {
                    var items = await _context.CollectionItems.Where(a => a.CollectionId == entity.Id).OrderByDescending(a => a.CollectionItemId).Take(10).ToListAsync();
                    var hasItems = items.Any();
                    var page = new ASObject();
                    page["type"].Add(new ASTerm("OrderedCollectionPage"));
                    page["id"].Add(new ASTerm(entity.Id + "?from_id=" + (hasItems ? items.First().CollectionItemId + 1 : 0)));
                    page["partOf"].Add(new ASTerm(entity.Id));
                    if (collection["attributedTo"].Any())
                        page["attributedTo"].Add(collection["attributedTo"].First());
                    if (items.Count > 0)
                        page["next"].Add(new ASTerm(entity.Id + "?from_id=" + items.Last().CollectionItemId));
                    page["orderedItems"].AddRange(items.Select(a => new ASTerm(a.ElementId)));

                    collection["first"].Add(new ASTerm(page));
                }

                return collection;
            }
        }
    }
}
