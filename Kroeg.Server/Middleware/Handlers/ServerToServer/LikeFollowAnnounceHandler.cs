using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.DependencyInjection;

namespace Kroeg.Server.Middleware.Handlers.ServerToServer
{
    public class LikeFollowAnnounceHandler : BaseHandler
    {
        private readonly CollectionTools _collection;
        private readonly EntityData _data;
        private readonly RelevantEntitiesService _relevantEntities;
        private readonly IServiceProvider _serviceProvider;

        public LikeFollowAnnounceHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, CollectionTools collection, EntityData data, RelevantEntitiesService relevantEntities, IServiceProvider serviceProvider) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _collection = collection;
            _data = data;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type == "Follow")
            {
                if (Actor.Data["_:locked"].Any(a => !(bool) a.Primitive) || Actor.Data["locked"].Any(a => !(bool) a.Primitive))
                {
                    var accept = new ASObject();
                    accept.Replace("type", new ASTerm("Accept"));
                    accept.Replace("actor", new ASTerm(Actor.Id));
                    accept.Replace("object", new ASTerm(MainObject.Id));

                    var claims = new ClaimsPrincipal();
                    var handler = ActivatorUtilities.CreateInstance<GetEntityMiddleware.GetEntityHandler>(_serviceProvider, claims);
                    var outbox = await EntityStore.GetEntity((string)Actor.Data["outbox"].First().Primitive, false);
                    await handler.ClientToServer(outbox, accept);
                }

                return true;
            }

            if (MainObject.Type != "Like" && MainObject.Type != "Announce") return true;

            var toFollowOrLike = await EntityStore.GetEntity((string) MainObject.Data["object"].Single().Primitive, false);
            if (toFollowOrLike == null || !toFollowOrLike.IsOwner) return true; // not going to update side effects now.

            // sent to not the owner, so not updating!
            if ((string)toFollowOrLike.Data["attributedTo"].Single().Primitive != Actor.Id) return true;

            string collectionId = null, objectToAdd = null;

            switch (MainObject.Type)
            {
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

            if (entityToAdd == null) throw new InvalidOperationException("Can't like or announce a non-existant object!");

            await _collection.AddToCollection(collection, entityToAdd);

            return true;
        }
    }
}
