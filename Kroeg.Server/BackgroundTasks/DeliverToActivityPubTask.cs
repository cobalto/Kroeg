using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Newtonsoft.Json;
using Kroeg.Server.Services;
using Kroeg.ActivityStreams;
using Kroeg.Server.Middleware;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Linq;
using Kroeg.Server.Salmon;

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
        private readonly EntityFlattener _entityFlattener;
        private readonly IServiceProvider _serviceProvider;
        private readonly DeliveryService _deliveryService;
        private readonly APContext _context;
        private readonly EntityData _data;
        private readonly SignatureVerifier _verifier;

        public DeliverToActivityPubTask(EventQueueItem item, IEntityStore entityStore, EntityFlattener entityFlattener, IServiceProvider serviceProvider, DeliveryService deliveryService, APContext context, EntityData data, SignatureVerifier verifier) : base(item)
        {
            _entityStore = entityStore;
            _entityFlattener = entityFlattener;
            _serviceProvider = serviceProvider;
            _deliveryService = deliveryService;
            _context = context;
            _data = data;
            _verifier = verifier;
        }

        private async Task<string> _buildSignature(string ownerId, HttpRequestMessage message)
        {
            string[] headers = new string[] { "(request-target)", "date", "authorization", "content-type" };
            var toSign = new StringBuilder();
            foreach (var header in headers)
            {
                if (header == "(request-target)")
                    toSign.Append($"{header}: {message.Method.Method.ToLower()} {message.RequestUri.PathAndQuery}\n");
                else
                {
                    if (message.Headers.TryGetValues(header, out var vals))
                        toSign.Append($"{header}: {string.Join(", ", vals)}\n");
                    else if (message.Content.Headers.TryGetValues(header, out var cvals))
                        toSign.Append($"{header}: {string.Join(", ", cvals)}\n");
                    else
                        toSign.Append($"{header}: \n");
                }
            }

            toSign.Remove(toSign.Length - 1, 1);

            var key = await _context.GetKey(ownerId);
            var magic = new MagicKey(key.PrivateKey);
            var signed = Convert.ToBase64String(magic.Sign(Encoding.UTF8.GetBytes(toSign.ToString())));

            var ownerOrigin = new Uri(ownerId);
            var keyId = $"{ownerOrigin.Scheme}://{ownerOrigin.Host}{_data.BasePath}auth/key?id={Uri.EscapeDataString(ownerId)}";

            return $"keyId=\"{keyId}\",algorithm=\"rsa-sha256\",headers=\"{string.Join(" ", headers)}\",signature=\"{signed}\"";
        }

        public async Task PostToServer()
        {
            var entity = await _entityStore.GetEntity(Data.ObjectId, false);
            var owner = await _entityStore.GetEntity((string) entity.Data["actor"].First().Primitive, false);
            var unflattened = await _entityFlattener.Unflatten(_entityStore, entity);

            var token = await _verifier.BuildJWS(owner, Data.TargetInbox);

            var hc = new HttpClient();
            var serialized = unflattened.Serialize(true).ToString(Formatting.None);
            var content = new StringContent(serialized, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/ld+json");
            content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("profile", "\"https://www.w3.org/ns/activitystreams\""));

            var message = new HttpRequestMessage(HttpMethod.Post, Data.TargetInbox);
            message.Content = content;

            message.Headers.Date = DateTimeOffset.Now;
            message.Headers.TryAddWithoutValidation("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            message.Headers.Add("Signature", await _buildSignature(owner.Id, message));

            var result = await hc.SendAsync(message);

            var resultContent = await result.Content.ReadAsStringAsync();
            if (!result.IsSuccessStatusCode && (int)result.StatusCode / 100 == 5)
                throw new Exception("Failed to deliver. Retrying later.");
        }

        public override async Task Go()
        {
            var inbox = await _entityStore.GetEntity(Data.TargetInbox, false);
            if (inbox != null && inbox.IsOwner && inbox.Type == "_inbox" && false)
            {
                var item = await _entityStore.GetEntity(Data.ObjectId, false);

                var claims = new ClaimsPrincipal();
                var handler = ActivatorUtilities.CreateInstance<GetEntityMiddleware.GetEntityHandler>(_serviceProvider, claims);
                try
                {
                    await handler.ServerToServer(inbox, item.Data);
                }
                catch (UnauthorizedAccessException) { }
                catch (InvalidOperationException) { }
            }
            else
            {
                await PostToServer();
            }
        }
    }
}
