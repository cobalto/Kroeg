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
    public class UndoHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public UndoHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Undo") return true;

            var toUndo = await EntityStore.GetEntity((string) MainObject.Data["object"].Single().Primitive, true);
            if (toUndo == null) throw new InvalidOperationException("Well, I can't undo an unknown object.");

            string collectionId = null, objectToAdd = null;

            var toFollowOrLike = await EntityStore.GetEntity((string) toUndo.Data["object"].Single().Primitive, true);
            if (toFollowOrLike == null || !toFollowOrLike.IsOwner) return true; // can't undo side effects.
            if ((toUndo.Type == "Follow" && Actor.Id != toFollowOrLike.Id) || (toUndo.Type != "Follow" && (string)toFollowOrLike.Data["attributedTo"].Single().Primitive != Actor.Id)) return true;

            if (toUndo.Type == "Follow")
            {
                collectionId = (string) toFollowOrLike.Data["followers"].SingleOrDefault()?.Primitive;
                objectToAdd = (string) toFollowOrLike.Data["actor"].Single().Primitive;
            }
            else if (toUndo.Type == "Like")
            {
                collectionId = (string)toFollowOrLike.Data["likes"].SingleOrDefault()?.Primitive;
                objectToAdd = toUndo.Id;
            }
            else if (toUndo.Type == "Announce")
            {
                collectionId = (string)toFollowOrLike.Data["shares"].SingleOrDefault()?.Primitive;
                objectToAdd = toUndo.Id;
            }

            if (collectionId == null) return true; // no way to store followers/likse

            var collection = await EntityStore.GetEntity(collectionId, false);
            await _collection.RemoveFromCollection(collection, objectToAdd);

            return true;
        }
    }
}
