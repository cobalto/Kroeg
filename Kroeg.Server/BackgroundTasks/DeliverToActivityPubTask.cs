using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.BackgroundTasks
{
    public class DeliverToActivityPubData
    {
        public string TargetInbox { get; set; }
        public string ObjectId { get; set; }
    }

    public class DeliverToActivityPubTask : BaseTask<DeliverToActivityPubData, DeliverToActivityPubTask>
    {
        private readonly IEntityStore _entityStore;

        public DeliverToActivityPubTask(EventQueueItem item, IEntityStore entityStore) : base(item)
        {
            _entityStore = entityStore;
        }

        public override async Task Go()
        {
            var entity = await _entityStore.GetEntity(Data.ObjectId, false);

            var hc = new HttpClient();
            var serialized = entity.SerializedData;
            var content = new StringContent(serialized, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/ld+json");
            content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("profile", "\"https://www.w3.org/ns/activitystreams\""));
            content.Headers.TryAddWithoutValidation("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");

            var result = await hc.PostAsync(Data.TargetInbox, content);
            var resultContent = await result.Content.ReadAsStringAsync();
            if (!result.IsSuccessStatusCode && (int)result.StatusCode / 100 == 5)
                throw new Exception("Failed to deliver. Retrying later.");
        }
    }
}
