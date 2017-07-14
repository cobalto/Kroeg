using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class CreateActivityHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly EntityData _entityData;

        public CreateActivityHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, EntityData entityData) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _entityData = entityData;
        }

        private async Task AddCollection(ASObject entity, string obj, string type, string parent)
        {
            var collection = await _collection.NewCollection(EntityStore, null, type, parent);
            await EntityStore.StoreEntity(collection);

            entity.Replace(obj, new ASTerm(collection.Id));
        }

        private void _merge(List<ASTerm> to, List<ASTerm> from)
        {
            var str = new HashSet<string>(to.Select(a => (string)a.Primitive).Concat(from.Select(a => (string) a.Primitive)));

            to.Clear();
            to.AddRange(str.Select(a => new ASTerm(a)));
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Create") return true;

            var activityData = MainObject.Data;
            var objectEntity = await EntityStore.GetEntity((string) activityData["object"].First().Primitive, false);
            var objectData = objectEntity.Data;

            if (_entityData.IsActivity(objectData)) throw new InvalidOperationException("Cannot Create another activity!");

            objectData["attributedTo"].AddRange(activityData["actor"]);

            await AddCollection(objectData, "likes", "_likes", objectEntity.Id);
            await AddCollection(objectData, "shares", "_shares", objectEntity.Id);
            await AddCollection(objectData, "replies", "_replies", objectEntity.Id);

            _merge(activityData["to"], objectData["to"]);
            _merge(activityData["bto"], objectData["bto"]);
            _merge(activityData["cc"], objectData["cc"]);
            _merge(activityData["bcc"], objectData["bcc"]);
            _merge(activityData["audience"], objectData["audience"]);

            objectData.Replace("published", new ASTerm(DateTime.Now.ToString("o")));

            objectEntity.Data = objectData;
            MainObject.Data = activityData;

            return true;
        }
    }
}
