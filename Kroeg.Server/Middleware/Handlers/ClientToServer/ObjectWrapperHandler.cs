using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class ObjectWrapperHandler : BaseHandler
    {
        private readonly EntityData _entityData;

        public ObjectWrapperHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, EntityData entityData)
            : base(entityStore, mainObject, actor, targetBox)
        {
            _entityData = entityData;
        }

        public override async Task<bool> Handle()
        {
            if (_entityData.IsActivity(MainObject.Type)) return true;
            var data = MainObject.Data;

            var createActivity = new ASObject();
            createActivity["type"].Add(new ASTerm("Create"));
            createActivity["to"].AddRange(data["to"]);
            createActivity["bto"].AddRange(data["bto"]);
            createActivity["cc"].AddRange(data["cc"]);
            createActivity["bcc"].AddRange(data["bcc"]);
            createActivity["audience"].AddRange(data["audience"]);
            createActivity["actor"].Add(new ASTerm(Actor.Id));
            createActivity["object"].Add(new ASTerm(MainObject.Id));
            createActivity["id"].Add(new ASTerm(_entityData.UriFor(createActivity)));

            var activityEntity = await EntityStore.StoreEntity(APEntity.From(createActivity, true));
            MainObject = activityEntity;

            return true;
        }
    }
}
