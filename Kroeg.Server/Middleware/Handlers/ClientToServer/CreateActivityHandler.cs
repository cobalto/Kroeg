using System;
using System.Collections.Generic;
using System.Linq;
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

        public CreateActivityHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, CollectionTools collection) : base(entityStore, mainObject, actor, targetBox)
        {
            _collection = collection;
        }

        private async Task AddCollection(ASObject entity, string obj, string type)
        {
            var collection = _collection.NewCollection(null, type);
            await EntityStore.StoreEntity(collection);

            entity.Replace(obj, new ASTerm(collection.Id));
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Create") return true;

            var activityData = MainObject.Data;
            var objectEntity = await EntityStore.GetEntity((string) activityData["object"].First().Primitive, false);
            var objectData = objectEntity.Data;

            objectData["attributedTo"].AddRange(activityData["actor"]);

            await AddCollection(objectData, "likes", "_likes");
            await AddCollection(objectData, "shares", "_shares");
            await AddCollection(objectData, "replies", "_replies");

            objectData.Replace("published", new ASTerm(DateTime.Now.ToString("o")));

            objectEntity.Data = objectData;

            return true;
        }
    }
}
