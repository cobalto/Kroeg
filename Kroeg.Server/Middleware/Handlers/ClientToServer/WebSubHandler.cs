using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Kroeg.Server.Middleware.Handlers.ClientToServer
{
    public class WebSubHandler : BaseHandler
    {
        private readonly APContext _context;

        public WebSubHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox, ClaimsPrincipal user, APContext context) : base(entityStore, mainObject, actor, targetBox, user)
        {
            _context = context;
        }

        public override async Task<bool> Handle()
        {
            var activity = MainObject;
            if (MainObject.Type == "Undo")
            {
                var subObject = await EntityStore.GetEntity((string)activity.Data["object"].First().Primitive, false);
                if (subObject?.Type != "Follow") return true;

                activity = subObject;
            }
            else if (MainObject.Type != "Follow") return true;

            var target = await EntityStore.GetEntity((string)activity.Data["object"].First().Primitive, true);
            if (target == null) return true; // can't really fix subscriptions on a thing that doesn't exist

                var hubUrl = (string) target.Data["_:hubUrl"].SingleOrDefault()?.Primitive;
                if (hubUrl == null) return true;

                var taskEvent = WebSubBackgroundTask.Make(new WebSubBackgroundData { Unsubscribe = MainObject.Type == "Undo", ToFollowID = target.Id, ActorID = (string)MainObject.Data["actor"].Single().Primitive });
                _context.EventQueue.Add(taskEvent);
                await _context.SaveChangesAsync();

            return true;
        }
    }
}
