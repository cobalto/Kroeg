using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.Shared
{
    public class CommitChangesHandler : BaseHandler
    {
        public CommitChangesHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox) : base(entityStore, mainObject, actor, targetBox)
        {
        }

        public override async Task<bool> Handle()
        {
            await EntityStore.CommitChanges();
            return true;
        }
    }
}
