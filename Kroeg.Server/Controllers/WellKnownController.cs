using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Net.Http;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
    [Route(".well-known")]
    public class WellKnownController : Controller
    {
        private readonly APContext _context;
        private readonly IEntityStore _entityStore;
        private readonly EntityData _entityData;

        public WellKnownController(APContext context, IEntityStore entityStore, EntityData entityData)
        {
            _context = context;
            _entityStore = entityStore;
            _entityData = entityData;
        }

        public class WebfingerLink
        {
            public string rel { get; set; }
            public string type { get; set; }
            public string href { get; set; }
            public string template { get; set; }
        }

        public class WebfingerResult
        {
            public string subject { get; set; }
            public List<string> aliases { get; set; }
            public List<WebfingerLink> links { get; set; }
        }

        private class _queryParam
        {
            public string preferredUsername { get; set; }
        }


        [HttpPost("hub")]
        public async Task<IActionResult> ProcessPushRequest()
        {
            var userId = (string) HttpContext.Items["fullPath"];
            var user = await _entityStore.GetEntity(userId, false);
            if (user == null)
                return StatusCode(400, "this is not a valid hub");

            var callback = Request.Form["hub.callback"].First();
            var mode = Request.Form["hub.mode"].First();
            var topic = Request.Form["hub.topic"].First();
            var lease_seconds = Request.Form["hub.lease_seconds"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(lease_seconds)) lease_seconds = "86400";
            var secret = Request.Form["hub.secret"].FirstOrDefault();

            if (mode != "unsubscribe" && mode != "subscribe")
                return StatusCode(400, "bad hub.mode");

            await _continueVerify(mode, callback, topic, lease_seconds, secret, user);
            return Accepted();
        }

        private async Task _continueVerify(string mode, string callback, string topic, string lease_seconds, string secret, APEntity user)
        {
            await Task.Delay(2000);
            var hc = new HttpClient();

            var testurl = callback;
            if (callback.Contains("?"))
                testurl += "&";
            else
                testurl += "?";


            string challenge = Guid.NewGuid().ToString();
            testurl += $"hub.mode={mode}&hub.topic={Uri.EscapeDataString(topic)}&hub.lease_seconds={lease_seconds}&hub.challenge={Uri.EscapeDataString(challenge)}";

            var result = await hc.GetAsync(testurl);
            if (!result.IsSuccessStatusCode)
                return;

            if (await result.Content.ReadAsStringAsync() != challenge)
                return;

            WebsubSubscription subscription = await _context.WebsubSubscriptions.FirstOrDefaultAsync(a => a.Callback == callback);

            if (subscription != null)
            {
                subscription.Expiry = DateTime.Now.AddSeconds(int.Parse(lease_seconds ?? "86400"));
                subscription.Secret = secret;
                if (mode == "unsubscribe")
                    _context.WebsubSubscriptions.Remove(subscription);
            }
            else if (mode == "subscribe")
            {
                subscription = new WebsubSubscription()
                {
                    Callback = callback,
                    Expiry = DateTime.Now.AddSeconds(int.Parse(lease_seconds ?? "86400")),
                    Secret = secret,
                    User = user
                };
                _context.WebsubSubscriptions.Add(subscription);
            }

            await _context.SaveChangesAsync();
        }

        [HttpGet("webfinger")]
        public async Task<IActionResult> WebFinger(string resource)
        {
            if (!resource.StartsWith("acct:")) return Unauthorized();

            var username = resource.Split(':')[1].Split('@');

            var param = new _queryParam() { preferredUsername = username[0] };

            var items = await _context.Entities.FromSql("SELECT * from \"Entities\" WHERE \"SerializedData\" @> {0}::jsonb", JsonConvert.SerializeObject(param))
                .Where(a => a.Type == "Person").ToListAsync();
            if (items.Count == 0) return NotFound();

            var item = items.First();

            var outbox = (string)item.Data["outbox"].First().Primitive + $".atom?from_id={int.MaxValue}";
            var inbox = (string)item.Data["inbox"].First().Primitive;

            var result = new WebfingerResult()
            {
                subject = resource,
                aliases = new List<string>() { item.Id },
                links = new List<WebfingerLink>
                {
                    new WebfingerLink
                    {
                        rel = "http://webfinger.net/rel/profile-page",
                        type = "text/html",
                        href = item.Id
                    },

                    new WebfingerLink
                    {
                        rel = "http://schemas.google.com/g/2010#updates-from",
                        type = "application/atom+xml",
                        href = outbox
                    },

                    new WebfingerLink
                    {
                        rel = "self",
                        type = "application/activity+json",
                        href = item.Id
                    },

                    new WebfingerLink
                    {
                        rel = "self",
                        type = "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"",
                        href = item.Id
                    },

                    new WebfingerLink
                    {
                        rel = "salmon",
                        href = inbox + ".atom"
                    },

                    new WebfingerLink
                    {
                        rel = "magic-public-key",
                        // just to satisfy Mastodon for now, we don't actually send salmons right now
                        href = "data:application/magic-public-key,RSA.3Jne_dchpMg9BhQJgYdomcEHwNs_bAFUAjAIz2mEnmMQAn3su3NlyKkENj0kqLLixAT1eRl6WqG0f7N2puNKqeVF_VwiTK5rqOfJNX4JtV2AwreFj2orafTBU1DR35Bth8kd6Vpc9y-1GEveeLs46BxG1LghwjjWmz9itNko9asMpVrQR3vq_BASB4bTrhASdPkvyCZe3DzibsDFXpM8jhmj0nKRxj_m9mllsz-fKYF7VEAdhfjiSi0EJMAaqBABwXyJJGEmI7i6jJW_ilN-9uzOXbE9ozhv7D13Ko6rlEcYA-IXSd6AfVOMX_BgTrVphoV19LtaQwVTc7VUDfMWMQ==.AQAB"
                    },

                    new WebfingerLink
                    {
                        rel = "http://ostatus.org/schema/1.0/subscribe",
                        template = item.Id + "?subscribe&user={uri}"
                    }
                }
            };

            return Json(result);
        }

        [HttpGet("host-meta")]
        public IActionResult GetHostMeta()
        {
            Response.ContentType = "application/xrd+xml";

            return Ok("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
"<XRD xmlns=\"http://docs.oasis-open.org/ns/xri/xrd-1.0\">" +
$" <Link rel=\"lrdd\" type=\"application/jrd+json\" template=\"https://{_entityData.BaseDomain}/.well-known/webfinger?resource={{uri}}\"/>" +
"</XRD>");
        }
    }
}
