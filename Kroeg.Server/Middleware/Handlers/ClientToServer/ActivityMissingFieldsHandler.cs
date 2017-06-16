using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class ActivityMissingFieldsHandler : BaseHandler
    {
        public ActivityMissingFieldsHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox) : base(entityStore, mainObject, actor, targetBox)
        {
        }

        public override async Task<bool> Handle()
        {
            await Task.Yield();

            var data = MainObject.Data;
            if (!data["actor"].Any())
                data["actor"].Add(new ASTerm(Actor.Id));

            // add published and updated.
            data.Replace("published", new ASTerm(DateTime.Now.ToString("o")));

            MainObject.Data = data;

            return true;
        }
    }
}
