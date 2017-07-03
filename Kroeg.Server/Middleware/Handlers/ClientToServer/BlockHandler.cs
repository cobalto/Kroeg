using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class BlockHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public BlockHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Block") return true;
            var activityData = MainObject.Data;

            var toBlock = (string) activityData["object"].First().Primitive;
            var entity = await EntityStore.GetEntity(toBlock, true);
            if (entity == null) throw new InvalidOperationException("Cannot block a non-existant object");

            var blockscollection = await EntityStore.GetEntity((string)Actor.Data["blocks"].First().Primitive, false);
            await _collection.AddToCollection(blockscollection, MainObject);

            var blockedcollection = await EntityStore.GetEntity((string)blockscollection.Data["_blocked"].First().Primitive, false);
            await _collection.AddToCollection(blockedcollection, entity);

            return true;
        }
    }
}
