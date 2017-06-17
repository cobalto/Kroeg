using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ServerToServer
{
    public class LikeFollowAnnounceHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public LikeFollowAnnounceHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Follow" && MainObject.Type != "Like" && MainObject.Type != "Announce") return true;

            var toFollowOrLike = await EntityStore.GetEntity((string) MainObject.Data["object"].Single().Primitive, false);
            if (toFollowOrLike == null || !toFollowOrLike.IsOwner) return true; // not going to update side effects now.

            string collectionId = null, objectToAdd = null;

            switch (MainObject.Type)
            {
                case "Follow":
                    collectionId = (string) toFollowOrLike.Data["followers"].SingleOrDefault()?.Primitive;
                    objectToAdd = (string) MainObject.Data["actor"].Single().Primitive;
                    break;
                case "Like":
                    collectionId = (string) toFollowOrLike.Data["likes"].SingleOrDefault()?.Primitive;
                    objectToAdd = MainObject.Id;
                    break;
                case "Announce":
                    collectionId = (string)toFollowOrLike.Data["shares"].SingleOrDefault()?.Primitive;
                    objectToAdd = MainObject.Id;
                    break;
            }

            if (collectionId == null) return true; // no way to store followers/likse

            var collection = await EntityStore.GetEntity(collectionId, false);
            var entityToAdd = await EntityStore.GetEntity(objectToAdd, true);

            if (entityToAdd == null) throw new InvalidOperationException("Can't follow or like a null object!");

            await _collection.AddToCollection(collection, entityToAdd);

            return true;
        }
    }
}
