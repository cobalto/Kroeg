using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class FollowLikeHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public FollowLikeHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Like" && MainObject.Type != "Follow") return true;

            var userData = Actor.Data;
            string targetCollectionId = null;
            if (MainObject.Type == "Like")
                targetCollectionId = (string)userData["likes"].Single().Primitive;
            else if (MainObject.Type == "Follow")
                targetCollectionId = (string)userData["following"].Single().Primitive;

            var targetCollection = await EntityStore.GetEntity(targetCollectionId, false);
            var objectEntity = await EntityStore.GetEntity((string) MainObject.Data["object"].Single().Primitive, true);
            if (objectEntity == null) throw new InvalidOperationException($"Cannot {MainObject.Type.ToLower()} a non-existant object!");

            await _collection.AddToCollection(targetCollection, objectEntity);
            return true;
        }
    }
}
