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
using Kroeg.Server.Salmon;

namespace Kroeg.Server.BackgroundTasks
{
    public class DeliverToSalmonData
    {
        public string SalmonUrl { get; set; }
        public string EntityId { get; set; }
    }

    public class DeliverToSalmonTask : BaseTask<DeliverToSalmonData, DeliverToSalmonTask>
    {
        private readonly IEntityStore _entityStore;
        private readonly APContext _context;
        private readonly AtomEntryGenerator _entryGenerator;

        public DeliverToSalmonTask(EventQueueItem item, IEntityStore entityStore, APContext context, AtomEntryGenerator entryGenerator) : base(item)
        {
            _entityStore = entityStore;
            _context = context;
            _entryGenerator = entryGenerator;
        }

        public override async Task Go()
        {
            var entity = await _entityStore.GetEntity(Data.EntityId, false);
            if (entity == null) return;

            var actorId = entity.Data["actor"].Concat(entity.Data["attributedTo"]).FirstOrDefault();
            if (actorId == null) return; // ???

            var key = await _context.GetKey((string) actorId.Primitive);

            var doc = await _entryGenerator.Build(entity.Data);

            var envelope = new MagicEnvelope(doc.ToString(), "application/atom+xml", new MagicKey(key.PrivateKey));
            var hc = new HttpClient();

            var content = new StringContent(envelope.Build().ToString(), Encoding.UTF8, "application/magic-envelope+xml");
            var response = await hc.PostAsync(Data.SalmonUrl, content);

            if (((int)response.StatusCode) / 100 == 5)
                throw new Exception("try later");
        }
    }
}
