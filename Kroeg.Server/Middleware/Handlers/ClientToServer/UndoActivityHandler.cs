using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class UndoActivityHandler : BaseHandler
    {
        private readonly CollectionTools _collection;

        public UndoActivityHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Undo") return true;
            var toUndoId = (string) MainObject.Data["object"].Single().Primitive;
            var toUndo = await EntityStore.GetEntity(toUndoId, false);

            if (toUndo == null || !toUndo.IsOwner) throw new InvalidOperationException("Object to undo does not exist!");
            if (toUndo.Type != "Like" && toUndo.Type != "Follow" && toUndo.Type != "Block") throw new InvalidOperationException("Cannot undo this type of object!");
            if (!toUndo.Data["actor"].Any(a => (string) a.Primitive == Actor.Id)) throw new InvalidOperationException("You are not allowed to undo this activity!");

            var userData = Actor.Data;
            string targetCollectionId = null;
            if (toUndo.Type == "Block")
            {
                var blocksCollection = await EntityStore.GetEntity((string)userData["blocks"].Single().Primitive, false);
                var blockedCollection = await EntityStore.GetEntity((string)blocksCollection.Data["_blocked"].Single().Primitive, false);

                await _collection.RemoveFromCollection(blocksCollection, toUndo);

                var blockedUser = (string) toUndo.Data["object"].Single().Primitive;
                await _collection.RemoveFromCollection(blockedCollection, blockedUser);

                return true;
            }
            else if (toUndo.Type == "Like")
                targetCollectionId = (string)userData["likes"].Single().Primitive;
            else if (toUndo.Type == "Follow")
                targetCollectionId = (string)userData["following"].Single().Primitive;

            var targetCollection = await EntityStore.GetEntity(targetCollectionId, false);
            var targetId = (string) MainObject.Data["object"].Single().Primitive;

            await _collection.RemoveFromCollection(targetCollection, targetId);
            return true;
        }
    }
}
