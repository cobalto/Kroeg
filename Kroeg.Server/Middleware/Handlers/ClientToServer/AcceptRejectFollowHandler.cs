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
    public class AcceptRejectFollowHandler : BaseHandler
    {
        private readonly EntityData _data;
        private readonly CollectionTools _collection;

        public AcceptRejectFollowHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, EntityData data, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _data = data;
            _collection = collection;
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Accept" && MainObject.Type != "Reject") return true;

            var subObject = await EntityStore.GetEntity((string)MainObject.Data["object"].Single().Primitive, true);
            var requestedUser = await EntityStore.GetEntity((string) subObject.Data["actor"].First().Primitive, true);

            if (subObject.Type != "Follow") return true;

            if ((string)subObject.Data["object"].Single().Primitive != Actor.Id) throw new InvalidOperationException("Cannot Accept or Reject a Follow from another actor!");
            
            if (MainObject.Type != "Like" && MainObject.Type != "Follow") return true;
            var audience = DeliveryService.GetAudienceIds(MainObject.Data);
            if (!audience.Contains(requestedUser.Id))
                throw new InvalidOperationException("Accepts/Rejects of Follows should be sent to the actor of the follower!");

            bool isAccept = MainObject.Type == "Accept";
            var followers = await EntityStore.GetEntity((string)Actor.Data["followers"].Single().Primitive, false);

            if (isAccept && !await _collection.Contains(followers.Id, requestedUser.Id))
                await _collection.AddToCollection(followers, requestedUser);
            if (!isAccept && await _collection.Contains(followers.Id, requestedUser.Id))
                await _collection.RemoveFromCollection(followers, requestedUser);
            return true;
        }
    }
}
