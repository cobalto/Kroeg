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

namespace Kroeg.Server.Services
{
    public class DeliveryService
    {
        private readonly APContext _context;
        private readonly EntityData _configuration;
        private readonly CollectionTools _collectionTools;
        private readonly IEntityStore _store;

        public DeliveryService(APContext context, CollectionTools collectionTools, EntityData configuration, IEntityStore store)
        {
            _context = context;
            _collectionTools = collectionTools;
            _configuration = configuration;
            _store = store;
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

        public async Task QueueDeliveryForEntity(APEntity entity, int collectionId, bool forwardOnly = false)
        {
            var audienceInbox = await _buildAudienceInbox(entity.Data, forward: forwardOnly);
            // Is public post?
            if (audienceInbox.Item2 && !forwardOnly)
            {
                await _queueWebsubDelivery((string)entity.Data["actor"].First().Primitive, collectionId, entity.Id);
            }

            foreach (var target in audienceInbox.Item1)
                _queueInboxDelivery(target, entity);

            foreach (var salmon in audienceInbox.Item3)
                _queueSalmonDelivery(salmon, entity);

            await _context.SaveChangesAsync();
        }

        public static HashSet<string> GetAudienceIds(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));

            return new HashSet<string>(targetIds);
        }

        private async Task<Tuple<HashSet<string>, bool, HashSet<string>>> _buildAudienceInbox(ASObject @object, int depth = 3, bool forward = false)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));

            bool isPublic = targetIds.Contains("https://www.w3.org/ns/activitystreams#Public");
            targetIds.Remove("https://www.w3.org/ns/activitystreams#Public");

            var targets = new HashSet<string>();
            var stack = new Stack<Tuple<int, APEntity>>();
            var salmons = new HashSet<string>();
            foreach (var item in targetIds)
            {
                var entity = await _store.GetEntity(item, true);
                var data = entity.Data;
                // if it's local collectionTools, or we don't need the forwarding thing
                if (!forward || ((data["type"].Contains(new ASTerm("CollectionTools")) || data["type"].Contains(new ASTerm("OrderedCollection"))) && entity.IsOwner))
                    stack.Push(new Tuple<int, APEntity>(0, entity));
            }

            while (stack.Any())
            {
                var entity = stack.Pop();

                var data = entity.Item2.Data;
                if ((data["type"].Contains(new ASTerm("CollectionTools")) || data["type"].Contains(new ASTerm("OrderedCollection"))) && entity.Item2.IsOwner && entity.Item1 < depth)
                {
                    foreach (var item in await _collectionTools.GetAll(entity.Item2.Id))
                        stack.Push(new Tuple<int, APEntity>(entity.Item1 + 1, item));
                }
                else if (_configuration.IsActor(data))
                {
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
