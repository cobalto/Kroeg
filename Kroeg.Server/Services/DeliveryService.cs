using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Kroeg.Server.Configuration;

namespace Kroeg.Server.Services
{
    public class DeliveryService
    {
        private readonly APContext _context;
        private readonly EntityData _configuration;
        private readonly CollectionTools _collectionTools;
        private readonly IEntityStore _store;
        private readonly JwtTokenSettings _tokenSettings;
        private readonly RelevantEntitiesService _relevantEntities;

        public DeliveryService(APContext context, CollectionTools collectionTools, EntityData configuration, IEntityStore store, RelevantEntitiesService relevantEntities)
        {
            _context = context;
            _collectionTools = collectionTools;
            _configuration = configuration;
            _store = store;
            _relevantEntities = relevantEntities;
        }

        public static bool IsPublic(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));

            return targetIds.Contains("https://www.w3.org/ns/activitystreams#Public");
        }

        public async Task QueueDeliveryForEntity(APEntity entity, int collectionId, string ownedBy = null)
        {
            var audienceInbox = await _buildAudienceInbox(entity.Data, forward: ownedBy, actor: false);
            // Is public post?
            if (audienceInbox.Item2 && ownedBy == null)
            {
                await _queueWebsubDelivery((string)entity.Data["actor"].First().Primitive, collectionId, entity.Id);
            }

            foreach (var target in audienceInbox.Item1)
                _queueInboxDelivery(target, entity);

            foreach (var salmon in audienceInbox.Item3)
                _queueSalmonDelivery(salmon, entity);

            await _context.SaveChangesAsync();
        }

        public async Task<List<APEntity>> GetUsersForSharedInbox(ASObject objectToProcess)
        {
            var audience = GetAudienceIds(objectToProcess);
            var result = new HashSet<string>();
            foreach (var entity in audience)
            {
                List<APEntity> followers = null;
                var data = await _store.GetEntity(entity, false);
                if (data != null && data.IsOwner && data.Type == "_followers")
                {
                    followers = new List<APEntity> { await _store.GetEntity((string)data.Data["attributedTo"].Single().Primitive, false) };
                }
                else if (data == null || !data.IsOwner)
                {
                    followers = await _relevantEntities.FindEntitiesWithFollowerId(data.Id);
                }

                if (followers == null || followers.Count == 0) continue; // apparently not a follower list? giving up.

                foreach (var f in followers)
                {
                    var following = await _collectionTools.CollectionsContaining(f.Id, "_following");
                    foreach (var item in following)
                    {
                        result.Add((string)item.Data["attributedTo"].Single().Primitive);
                    }
                }
            }

            var resultList = new List<APEntity>();
            foreach (var item in result)
                resultList.Add(await _store.GetEntity(item, false));

            return resultList;
        }

        public static HashSet<string> GetAudienceIds(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["attributedTo"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["actor"].Select(a => (string)a.Primitive));

            return new HashSet<string>(targetIds);
        }

        private async Task<Tuple<HashSet<string>, bool, HashSet<string>>> _buildAudienceInbox(ASObject @object, int depth = 3, string forward = null, bool actor = true)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));

            if (!actor) targetIds.Remove((string)@object["actor"].First().Primitive);

            bool isPublic = targetIds.Contains("https://www.w3.org/ns/activitystreams#Public");
            targetIds.Remove("https://www.w3.org/ns/activitystreams#Public");

            var targets = new HashSet<string>();
            var stack = new Stack<Tuple<int, APEntity, bool>>();
            var salmons = new HashSet<string>();
            foreach (var item in targetIds)
            {
                var entity = await _store.GetEntity(item, true);
                var data = entity.Data;
                // if it's local collection, or we don't need the forwarding thing
                var iscollection = data["type"].Any(a => (string)a.Primitive == "Collection" || (string)a.Primitive == "OrderedCollection");
                var shouldForward = entity.IsOwner && (forward == null || data["attributedTo"].Any(a => (string)a.Primitive == forward));
                if (!iscollection || shouldForward)
                    stack.Push(new Tuple<int, APEntity, bool>(0, entity, false));
            }

            while (stack.Any())
            {
                var entity = stack.Pop();

                var data = entity.Item2.Data;
                var iscollection = data["type"].Any(a => (string)a.Primitive == "Collection" || (string)a.Primitive == "OrderedCollection");
                var shouldForward = entity.Item2.IsOwner && (forward == null || data["attributedTo"].Any(a => (string)a.Primitive == forward));
                var useSharedInbox = (entity.Item2.IsOwner && entity.Item2.Type == "_following");
                if ((iscollection && shouldForward) && entity.Item1 < depth)
                {
                    foreach (var item in await _collectionTools.GetAll(entity.Item2.Id))
                        stack.Push(new Tuple<int, APEntity, bool>(entity.Item1 + 1, item, useSharedInbox));
                }
                else if (forward == null && _configuration.IsActor(data))
                {
                    if (entity.Item3)
                    {
                        if (data["sharedInbox"].Any())
                            targets.Add((string)data["inbox"].First().Primitive);
                        continue;
                    }

                    if (data["inbox"].Any())
                        targets.Add((string)data["inbox"].First().Primitive);
                    else if (data["_:salmonUrl"].Any())
                        salmons.Add((string)data["_:salmonUrl"].First().Primitive);
                }
            }

            return new Tuple<HashSet<string>, bool, HashSet<string>>(targets, isPublic, salmons);
        }

        private void _queueInboxDelivery(string targetUrl, APEntity entity)
        {
            _context.EventQueue.Add(
                DeliverToActivityPubTask.Make(new DeliverToActivityPubData
                {
                    ObjectId = entity.Id,
                    TargetInbox = targetUrl
                }));
        }

        private void _queueSalmonDelivery(string targetUrl, APEntity entity)
        {
            _context.EventQueue.Add(
                DeliverToSalmonTask.Make(new DeliverToSalmonData
                {
                    EntityId = entity.Id,
                    SalmonUrl = targetUrl
                }));
        }

        private async Task _queueWebsubDelivery(string userId, int collectionItem, string objectId)
        {
            foreach (var sub in await _context.WebsubSubscriptions.Where(a => a.UserId == userId && a.Expiry > DateTime.Now).ToListAsync())
            {
                _context.EventQueue.Add(
                    DeliverToWebSubTask.Make(new DeliverToWebSubData
                    {
                        CollectionItem = collectionItem,
                        ObjectId = objectId,
                        SourceUserId = userId,
                        Subscription = sub.Id
                    }));
            }
        }
    }
}
