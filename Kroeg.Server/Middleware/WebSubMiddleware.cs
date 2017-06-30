using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Middleware.Handlers;
using Kroeg.Server.Middleware.Handlers.ClientToServer;
using Kroeg.Server.Middleware.Handlers.ServerToServer;
using Kroeg.Server.Middleware.Handlers.Shared;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Salmon;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Kroeg.Server.Middleware.Renderers;
using Kroeg.Server.BackgroundTasks;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Middleware
{
    public class WebSubMiddleware
    {
        private readonly RequestDelegate _next;

        public WebSubMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, IServiceProvider serviceProvider, IEntityStore entityStore, APContext db, EntityData entityData)
        {
            if (!context.Request.Query["hub.mode"].Any())
            {
                await _next(context);
                return;
            }
            if (entityData.RewriteRequestScheme) context.Request.Scheme = "https";
            var fullpath = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";
            fullpath = fullpath.Remove(fullpath.Length - 5); // remove .atom

            var box = await entityStore.GetEntity(fullpath, false);
            if (box == null || box.Type != "_inbox")
                goto error;
            var user = await entityStore.GetEntity((string)box.Data["attributedTo"].First().Primitive, false);
            if (user == null) goto error;

            var mode = context.Request.Query["hub.mode"].First();
            var topic = context.Request.Query["hub.topic"].First();
            var challenge = context.Request.Query["hub.challenge"].First();
            var lease_seconds = context.Request.Query["hub.lease_seconds"].FirstOrDefault();

            var leaseSpan = lease_seconds == null ? null : (TimeSpan?) TimeSpan.FromSeconds(int.Parse(lease_seconds));

            var obj = await db.WebSubClients.FirstOrDefaultAsync(a => a.Topic == topic && a.ForUserId == user.Id);
            if (obj == null)
                goto error;

            if (mode == "subscribe")
            {
                obj.Expiry = DateTime.Now + leaseSpan.Value;
                db.EventQueue.Add(WebSubBackgroundTask.Make(new WebSubBackgroundData { ActorID = user.Id, ToFollowID = obj.TargetUserId }));
                await db.SaveChangesAsync();

                context.Response.StatusCode = 200;
                await context.Response.WriteAsync(challenge);
            }

            return;
        error:
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("nah");
        }
    }
}
