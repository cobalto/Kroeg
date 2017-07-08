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
using Kroeg.Server.Middleware.Renderers;
using System.Threading;
using System.Security.Claims;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Middleware
{
    public class GetEntityMiddleware
    {
        private readonly RequestDelegate _next;
        private List<IConverterFactory> _converters;

        public GetEntityMiddleware(RequestDelegate next)
        {
            _next = next;
            _converters = new List<IConverterFactory>
            {
                new AS2ConverterFactory(),
                new SalmonConverterFactory(),
                new AtomConverterFactory(true)
            };
        }

        public async Task Invoke(HttpContext context, IServiceProvider serviceProvider, EntityData entityData, IEntityStore store)
        {
            var handler = ActivatorUtilities.CreateInstance<GetEntityHandler>(serviceProvider, context.User);
            if (entityData.RewriteRequestScheme) context.Request.Scheme = "https";

            var fullpath = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";
            foreach (var converterFactory in _converters)
            {
                if (converterFactory.FileExtension != null && fullpath.EndsWith("." + converterFactory.FileExtension))
                {
                    fullpath = fullpath.Substring(0, fullpath.Length - 1 - converterFactory.FileExtension.Length);
                    context.Request.Headers.Remove("Accept");
                    context.Request.Headers.Add("Accept", converterFactory.RenderMimeType);
                    break;
                }
            }

            if (context.Request.Headers["Accept"].Contains("text/event-stream"))
            {
                await handler.EventStream(context, fullpath);
                return;
            }

            if (context.Request.Method == "POST" && context.Request.ContentType.StartsWith("multipart/form-data"))
            {
                context.Items.Add("fullPath", fullpath);
                context.Request.Path = "/settings/uploadMedia";
                await _next(context);
                return;
            }

            if (context.Request.QueryString.Value == "?hub")
            {
                context.Items.Add("fullPath", fullpath);
                context.Request.Path = "/.well-known/hub";
                context.Request.QueryString = QueryString.Empty;
                await _next(context);
                return;
            }

            IConverter readConverter = null;
            IConverter writeConverter = null;
            bool needRead = context.Request.Method == "POST";
            var target = fullpath;
            APEntity targetEntity = null;
            targetEntity = await store.GetEntity(target, false);

            if (needRead)
            {
                if (targetEntity?.Type == "_inbox")
                    target = (string)targetEntity.Data["attributedTo"].Single().Primitive;
            }

            if (targetEntity == null)
            {
                await _next(context);
                return;
            }


            foreach (var converterFactory in _converters)
            {
                bool worksForWrite = converterFactory.CanRender && ConverterHelpers.GetBestMatch(converterFactory.MimeTypes, context.Request.Headers["Accept"]) != null; 
                bool worksForRead = needRead && converterFactory.CanParse && ConverterHelpers.GetBestMatch(converterFactory.MimeTypes, context.Request.ContentType) != null;

                if (worksForRead && worksForWrite && readConverter == null && writeConverter == null)
                {
                    readConverter = writeConverter = converterFactory.Build(serviceProvider, target);
                    break;
                }

                if (worksForRead && readConverter == null)
                    readConverter = converterFactory.Build(serviceProvider, target);

                if (worksForWrite && writeConverter == null)
                    writeConverter = converterFactory.Build(serviceProvider, target);
            }

            ASObject data = null;
            if (readConverter != null)
                data = await readConverter.Parse(context.Request.Body);

            if (data == null && needRead && targetEntity != null)
            {
                context.Response.StatusCode = 415;
                await context.Response.WriteAsync("Unknown mime type " + context.Request.ContentType);
                return;
            }

            var arguments = context.Request.Query;

            try
            {
                if (context.Request.Method == "GET" || context.Request.Method == "HEAD")
                {
                    data = await handler.Get(fullpath, arguments, context);
                }
                else if (context.Request.Method == "POST" && data != null)
                {
                    data = await handler.Post(context, fullpath, data);
                }
            }
            catch (UnauthorizedAccessException e)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync(e.Message);
            }
            catch (InvalidOperationException e)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync(e.Message);
            }

            if (context.Response.HasStarted)
                return;

            if (data != null)
            {
                if (context.Request.Method == "HEAD")
                {
                    context.Response.StatusCode = 200;
                    return;
                }

                if (writeConverter != null)
                    await writeConverter.Render(context.Request, context.Response, data);
                else if (context.Request.ContentType == "application/magic-envelope+xml")
                {
                    context.Response.StatusCode = 202;
                    await context.Response.WriteAsync("accepted");
                }
                else
                {
                    context.Request.Path = "/render";
                    context.Items["object"] = APEntity.From(data);
                    await _next(context);
                }
                return;
            }

            if (!context.Response.HasStarted)
            {
                await _next(context);
            }
        }
        public class GetEntityHandler
        {
            private readonly APContext _context;
            private readonly EntityFlattener _flattener;
            private readonly IEntityStore _mainStore;
            private readonly AtomEntryGenerator _entryGenerator;
            private readonly IServiceProvider _serviceProvider;
            private readonly EntityData _entityData;
            private readonly DeliveryService _deliveryService;
            private readonly ClaimsPrincipal _user;
            private readonly CollectionTools _collectionTools;
            private readonly INotifier _notifier;
            private readonly JwtTokenSettings _tokenSettings;

            public GetEntityHandler(APContext acontext, EntityFlattener flattener, IEntityStore mainStore,
                AtomEntryGenerator entryGenerator, IServiceProvider serviceProvider, DeliveryService deliveryService,
                EntityData entityData, ClaimsPrincipal user, CollectionTools collectionTools, INotifier notifier, JwtTokenSettings tokenSettings)
            {
                _context = acontext;
                _flattener = flattener;
                _mainStore = mainStore;
                _entryGenerator = entryGenerator;
                _serviceProvider = serviceProvider;
                _entityData = entityData;
                _deliveryService = deliveryService;
                _user = user;
                _collectionTools = collectionTools;
                _notifier = notifier;
                _tokenSettings = tokenSettings;
            }

            internal async Task<ASObject> Get(string url, IQueryCollection arguments, HttpContext context)
            {
                var store = _mainStore;
                if (store is RetrievingEntityStore)
                    store = ((RetrievingEntityStore)store).Next;

                var userId = _user.FindFirstValue(JwtTokenSettings.ActorClaim);
                var entity = await store.GetEntity(url, true);
                if (entity == null) return null;
                if (entity.Type == "_blocks" && !entity.Data["attributedTo"].Any(a => (string)a.Primitive == userId)) throw new UnauthorizedAccessException("Blocks are private!");
                if (entity.Type == "_blocked") throw new UnauthorizedAccessException("This collection is only used internally for optimization reasons");
                if (entity.Type == "OrderedCollection" || entity.Type.StartsWith("_")) return await _getCollection(entity, arguments);
                if (entity.IsOwner && _entityData.IsActor(entity.Data)) return _getActor(entity, context);
                var audience = DeliveryService.GetAudienceIds(entity.Data);

                if (entity.Data["attributedTo"].Concat(entity.Data["actor"]).All(a => (string)a.Primitive != userId) && !audience.Contains("https://www.w3.org/ns/activitystreams#Public") && (userId == null || !audience.Contains(userId)))
                {
                    throw new UnauthorizedAccessException("No access");
                }

                return entity.Data;
            }

            public async Task EventStream(HttpContext context, string fullpath)
            {
                var entity = await _mainStore.GetEntity(fullpath, false);
                var authToken = context.Request.Query["authorization"];
                if (authToken.Count == 0)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("No authorization token provided");
                    return;
                }

                var tokenHandler = new JwtSecurityTokenHandler();
                var claims = tokenHandler.ValidateToken(authToken[0], _tokenSettings.ValidationParameters, out SecurityToken validatedToken);
                var entityClaim = claims.FindFirstValue(JwtTokenSettings.ActorClaim);
                if (entityClaim == null) return;
                if (entity.Data["attributedTo"].TrueForAll(a => (string)a.Primitive != entityClaim))
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Not authorized to view this item live");
                    return;
                }

                if (entity.Type != "_inbox" && entity.Type != "_outbox")
                {
                    context.Response.StatusCode = 415;
                    await context.Response.WriteAsync("Cannot view this item live!");
                    return;
                }


                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.Add("X-Accel-Buffering", "no");
                var tokenSource = new CancellationTokenSource();
                ConcurrentQueue<string> toSend = new ConcurrentQueue<string>();

                Action<string> subscriptionCall = (item) =>
                {
                    toSend.Enqueue(item);
                    tokenSource.Cancel();
                };

                await _notifier.Subscribe($"collection/{fullpath}", subscriptionCall);
                context.RequestAborted.Register(() => tokenSource.Cancel());

                await context.Response.WriteAsync(": hello, world!\n");
                await context.Response.Body.FlushAsync();

                if (context.Request.Headers.ContainsKey("Last-Event-ID"))
                {
                    var lastEventId = context.Request.Headers["Last-Event-ID"];
                    var location = await _context.CollectionItems.FirstOrDefaultAsync(a => a.CollectionId == fullpath && a.ElementId == lastEventId);
                    if (location != null)
                    {
                        var itemsAfter = await _context.CollectionItems.Where(a => a.CollectionId == fullpath && a.CollectionItemId > location.CollectionItemId).ToListAsync();
                        foreach (var item in itemsAfter)
                            toSend.Enqueue(item.ElementId);
                    }
                }

                while (true)
                {
                    if (toSend.Count == 0)
                    {
                        await context.Response.WriteAsync(":keepalive\n");
                        await context.Response.Body.FlushAsync();
                    }
                    else
                    {
                        do
                        {
                            var success = toSend.TryDequeue(out var item);
                            if (success)
                            {
                                var stored = await _mainStore.GetEntity(item, false);
                                var unflattened = await _flattener.Unflatten(_mainStore, stored);
                                var serialized = unflattened.Serialize().ToString(Formatting.None);
                                await context.Response.WriteAsync($"id: {item}\ndata: {serialized}\n\n");
                                await context.Response.Body.FlushAsync();
                            }
                        } while (toSend.Count > 0);
                    }
                    try
                    {
                        await Task.Delay(15000, tokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        if (context.RequestAborted.IsCancellationRequested) break;
                    }

                    tokenSource.Dispose();
                    tokenSource = new CancellationTokenSource();
                }

                await _notifier.Unsubscribe($"collection/{fullpath}", subscriptionCall);
                context.Response.Body.Dispose();
            }

            private ASObject _getActor(APEntity entity, HttpContext context)
            {
                var data = entity.Data;

                var basePath = $"{context.Request.Protocol}://{context.Request.Host.ToUriComponent()}{_entityData.BasePath}";

                var endpoints = new ASObject();
                endpoints.Replace("oauthAuthorizationEndpoint", new ASTerm(basePath + "auth/oauth"));
                endpoints.Replace("oauthTokenEndpoint", new ASTerm(basePath + "auth/token"));
                endpoints.Replace("settingsEndpoint", new ASTerm(basePath + "settings/auth"));
                endpoints.Replace("uploadMedia", new ASTerm((string)data["outbox"].Single().Primitive));
                endpoints.Replace("relevantObjects", new ASTerm(basePath + "settings/relevant"));
                endpoints.Replace("id", new ASTerm((string)null));

                data.Replace("endpoints", new ASTerm(endpoints));
                return data;
            }

            private async Task<ASObject> _getCollection(APEntity entity, IQueryCollection arguments)
            {
                var from_id = arguments["from_id"].FirstOrDefault();
                var collection = entity.Data;
                bool seePrivate = collection["attributedTo"].Any() && _user.FindFirstValue(JwtTokenSettings.ActorClaim) == (string)collection["attributedTo"].First().Primitive;

                collection["current"].Add(new ASTerm(entity.Id));
                collection["totalItems"].Add(new ASTerm(await _collectionTools.Count(entity.Id)));

                if (from_id != null)
                {
                    var fromId = int.Parse(from_id);
                    var items = await _collectionTools.GetItems(entity.Id, fromId, 10);
                    var hasItems = items.Any();
                    var page = new ASObject();
                    page["type"].Add(new ASTerm("OrderedCollectionPage"));
                    page["summary"].Add(new ASTerm("A collection"));
                    page["id"].Add(new ASTerm(entity.Id + "?from_id=" + (hasItems ? fromId : 0)));
                    page["partOf"].Add(new ASTerm(collection));
                    if (collection["attributedTo"].Any())
                        page["attributedTo"].Add(collection["attributedTo"].First());
                    if (items.Count > 0)
                        page["next"].Add(new ASTerm(entity.Id + "?from_id=" + (items.Last().CollectionItemId - 1).ToString()));
                    page["orderedItems"].AddRange(items.Select(a => new ASTerm(a.ElementId)));

                    return page;
                }
                else
                {
                    var items = await _collectionTools.GetItems(entity.Id, count: 10);
                    var hasItems = items.Any();
                    var page = new ASObject();
                    page["type"].Add(new ASTerm("OrderedCollectionPage"));
                    page["id"].Add(new ASTerm(entity.Id + "?from_id=" + (hasItems ? items.First().CollectionItemId + 1 : 0)));
                    page["partOf"].Add(new ASTerm(entity.Id));
                    if (collection["attributedTo"].Any())
                        page["attributedTo"].Add(collection["attributedTo"].First());
                    if (items.Count > 0)
                        page["next"].Add(new ASTerm(entity.Id + "?from_id=" + (items.Last().CollectionItemId - 1)));
                    page["orderedItems"].AddRange(items.Select(a => new ASTerm(a.ElementId)));

                    collection["first"].Add(new ASTerm(page));
                }

                return collection;
            }

            internal async Task<ASObject> Post(HttpContext context, string fullpath, ASObject @object)
            {
                var original = await _mainStore.GetEntity(fullpath, false);
                 if (!original.IsOwner) return null;

                switch (original.Type)
                {
                    case "_inbox":
                        return await ServerToServer(original, @object);
                    case "_outbox":
                        var userId = original.Data["attributedTo"].FirstOrDefault() ?? original.Data["actor"].FirstOrDefault();
                        if (userId == null || _user.FindFirst(JwtTokenSettings.ActorClaim).Value ==
                            (string) userId.Primitive) return await _clientToServer(original, @object);
                        throw new UnauthorizedAccessException("Cannot post to the outbox of another actor");
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

            private static Semaphore _serverToServerMutex = new Semaphore(1, 1);

            public async Task<ASObject> ServerToServer(APEntity inbox, ASObject activity)
            {
                var stagingStore = new StagingEntityStore(_mainStore);
                var userId = (string) inbox.Data["attributedTo"].Single().Primitive;
                var user = await _mainStore.GetEntity(userId, false);

                APEntity flattened;

                var id = (string) activity["id"].Single().Primitive;
                flattened = await _mainStore.GetEntity(id, false);
                if (flattened == null)
                    flattened = await _flattener.FlattenAndStore(stagingStore, activity);

                var sentBy = (string)activity["actor"].First().Primitive;
                var blocks = await _mainStore.GetEntity((string) user.Data["blocks"].First().Primitive, false);
                if (await _collectionTools.Contains((string)blocks.Data["_blocked"].First().Primitive, sentBy))
                    throw new UnauthorizedAccessException("You are blocked.");

                _serverToServerMutex.WaitOne();

                try
                {
                    using (var transaction = _context.Database.BeginTransaction())
                    {
                        foreach (var type in _serverToServerHandlers)
                        {
                            var handler = (BaseHandler)ActivatorUtilities.CreateInstance(_serviceProvider, type,
                                stagingStore, flattened, user, inbox, _user);
                            var handled = await handler.Handle();
                            flattened = handler.MainObject;
                            if (!handled) break;
                        }

                        await _context.SaveChangesAsync();

                        transaction.Commit();

                        await _notifier.Notify("collection/" + inbox.Id, flattened.Id);
                        return flattened.Data;
                    }
                }
                finally
                {
                    _serverToServerMutex.Release();
                }
            }

            private readonly List<Type> _clientToServerHandlers = new List<Type>
            {
                typeof(ObjectWrapperHandler),
                typeof(ActivityMissingFieldsHandler),
                typeof(CreateActivityHandler),

                // commit changes before modifying collections
                typeof(UpdateDeleteActivityHandler),
                typeof(CommitChangesHandler),
                typeof(FollowLikeHandler),
                typeof(AddRemoveActivityHandler),
                typeof(UndoActivityHandler),
                typeof(BlockHandler),
                typeof(CreateActorHandler),
                typeof(DeliveryHandler),
                typeof(WebSubHandler)
            };

            private async Task<ASObject> _clientToServer(APEntity outbox, ASObject activity)
            {
                var stagingStore = new StagingEntityStore(_mainStore);
                var userId = (string) outbox.Data["attributedTo"].Single().Primitive;
                var user = await _mainStore.GetEntity(userId, false);

                if (!_entityData.IsActivity(activity))
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    if (_entityData.IsActivity((string) activity["type"].First().Primitive))
#pragma warning restore CS0618 // Type or member is obsolete
                    {
                        throw new UnauthorizedAccessException("Sending an Activity without an actor isn't allowed. Are you sure you wanted to do that?");
                    }

                    var createActivity = new ASObject();
                    createActivity["type"].Add(new ASTerm("Create"));
                    createActivity["to"].AddRange(activity["to"]);
                    createActivity["bto"].AddRange(activity["bto"]);
                    createActivity["cc"].AddRange(activity["cc"]);
                    createActivity["bcc"].AddRange(activity["bcc"]);
                    createActivity["audience"].AddRange(activity["audience"]);
                    createActivity["actor"].Add(new ASTerm(userId));
                    createActivity["object"].Add(new ASTerm(activity));
                    activity = createActivity;
                }

                if (activity["type"].Any(a => (string)a.Primitive == "Create"))
                {
                    activity["id"].Clear();
                    if (activity["object"].SingleOrDefault()?.SubObject != null)
                        activity["object"].Single().SubObject["id"].Clear();
                }

                var flattened = await _flattener.FlattenAndStore(stagingStore, activity);

                using (var transaction = _context.Database.BeginTransaction())
                {
                    foreach (var type in _clientToServerHandlers)
                    {
                        var handler = (BaseHandler)ActivatorUtilities.CreateInstance(_serviceProvider, type,
                            stagingStore, flattened, user, outbox, _user);
                        var handled = await handler.Handle();
                        flattened = handler.MainObject;
                        if (!handled) break;
                    }

                    await _context.SaveChangesAsync();

                    transaction.Commit();

                    await _notifier.Notify("collection/" + outbox.Id, flattened.Id);

                    return flattened.Data;
                }
            }
        }
    }
}
