using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class FollowLikeHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly EntityData _data;

        public FollowLikeHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, EntityData data) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _data = data;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Like") return true;

            var userData = Actor.Data;
            string targetCollectionId = null;
            if (MainObject.Type == "Like")
                targetCollectionId = (string)userData["likes"].Single().Primitive;

            if (targetCollectionId == null) return true;

            var targetCollection = await EntityStore.GetEntity(targetCollectionId, false);
            var objectEntity = await EntityStore.GetEntity((string) MainObject.Data["object"].Single().Primitive, true);
            if (objectEntity == null) throw new InvalidOperationException($"Cannot {MainObject.Type.ToLower()} a non-existant object!");

            await _collection.AddToCollection(targetCollection, objectEntity);
            return true;
        }
    }
}
