using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Services;
using System.Collections.Generic;

namespace Kroeg.Server.BackgroundTasks
{
    public class WebSubBackgroundData
    {
        public bool Unsubscribe { get; set; }
        public string ActorID { get; set; }
        public string ToFollowID { get; set; }
    }

    public class WebSubBackgroundTask : BaseTask<WebSubBackgroundData, WebSubBackgroundTask>
    {
        private readonly IEntityStore _entityStore;
        private readonly APContext _context;
        private readonly CollectionTools _collectionTools;

        public WebSubBackgroundTask(EventQueueItem item, IEntityStore entityStore, APContext context, CollectionTools collectionTools) : base(item)
        {
            _entityStore = entityStore;
            _context = context;
            _collectionTools = collectionTools;
        }

        public override async Task Go()
        {
            var targetActor = await _entityStore.GetEntity(Data.ToFollowID, true);
            var actor = await _entityStore.GetEntity(Data.ActorID, true);

            if (targetActor == null || actor == null) return;

            var hubUrl = (string) targetActor.Data["_:hubUrl"].FirstOrDefault()?.Primitive;
            var topicUrl = (string)targetActor.Data["_:atomRetrieveUrl"].FirstOrDefault()?.Primitive;
            if (hubUrl == null || topicUrl == null) return;

            var clientObject = await _context.WebSubClients.FirstOrDefaultAsync(a => a.ForUserId == Data.ActorID && a.TargetUserId == Data.ToFollowID);
            var hc = new HttpClient();
            if (Data.Unsubscribe)
            {
                if (clientObject.Expiry > DateTime.Now.AddMinutes(1))
                {
                    // send request
                    var content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["hub.mode"] = "unsubscribe",
                        ["hub.topic"] = topicUrl,
                        ["hub.secret"] = clientObject.Secret
                    });

                    await hc.PostAsync(hubUrl, content);

                }

                _context.WebSubClients.Remove(clientObject);
                return;
            }

            if (clientObject == null)
            {
                clientObject = new WebSubClient
                {
                    ForUserId = Data.ActorID,
                    TargetUserId = Data.ToFollowID,
                    Secret = Guid.NewGuid().ToString()
                };

                _context.WebSubClients.Add(clientObject);
            }

            clientObject.Topic = topicUrl;
            await _context.SaveChangesAsync();

            var subscribeContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["hub.mode"] = "subscribe",
                ["hub.topic"] = topicUrl,
                ["hub.secret"] = clientObject.Secret,
                ["hub.callback"] = ((string) actor.Data["inbox"].First().Primitive) + ".atom",
                ["hub.lease_seconds"] = TimeSpan.FromDays(1).TotalSeconds.ToString()
            });

            var response = await hc.PostAsync(hubUrl, subscribeContent);
            var respText = await response.Content.ReadAsStringAsync();
            if (((int)response.StatusCode) / 100 != 2) response.EnsureSuccessStatusCode();
        }
    }
}
