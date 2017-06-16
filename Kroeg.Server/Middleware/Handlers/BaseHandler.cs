using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers
{
    public abstract class BaseHandler
    {
        protected readonly StagingEntityStore  EntityStore;
        protected readonly APEntity Actor;
        protected readonly APEntity TargetBox;

        public APEntity MainObject { get; protected set; }

        protected BaseHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox)
        {
            EntityStore = entityStore;
            MainObject = mainObject;
            Actor = actor;
            TargetBox = targetBox;
        }

        public abstract Task<bool> Handle();
    }
}
