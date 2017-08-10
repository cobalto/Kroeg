using System.Net.Http;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Tools;
using Kroeg.Server.Middleware.Renderers;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Kroeg.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kroeg.Server.Services.EntityStore
{
    public class RetrievingEntityStore : IEntityStore
    {
        public IEntityStore Next { get; }

        private readonly EntityFlattener _entityFlattener;
        private readonly IServiceProvider _serviceProvider;
        private readonly HttpContext _context;

        public RetrievingEntityStore(IEntityStore next, EntityFlattener entityFlattener, IServiceProvider serviceProvider, IHttpContextAccessor contextAccessor)
        {
            Next = next;
            _entityFlattener = entityFlattener;
            _serviceProvider = serviceProvider;
            _context = contextAccessor?.HttpContext;
        }

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            if (id == "https://www.w3.org/ns/activitystreams#Public")
            {
                var aso = new ASObject();
                aso.Replace("type", new ASTerm("Collection"));
                aso.Replace("id", new ASTerm("https://www.w3.org/ns/activitystreams#Public"));

                var ent = APEntity.From(aso);
                return ent;
            }

            APEntity entity = null;
            if (Next != null) entity = await Next.GetEntity(id, doRemote);

            if (entity?.Type == "_:LazyLoad" && !doRemote) return null;
            if ((entity != null && entity.Type != "_:LazyLoad") || !doRemote) return entity;

            var loadUrl = id;

            if (entity?.Type == "_:LazyLoad")
                loadUrl = (string) entity.Data["href"].First().Primitive;

            if (loadUrl.StartsWith("tag:")) return null;

            var htc = new HttpClient();
            htc.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json, application/json, application/atom+xml, text/html");

            if (_context != null)
            {
                var signatureVerifier = _serviceProvider.GetRequiredService<SignatureVerifier>();
                var user = await Next.GetEntity(_context.User.FindFirstValue(JwtTokenSettings.ActorClaim), false);
                if (user != null)
                {
                    var jwt = await signatureVerifier.BuildJWS(user, id);
                    htc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
                }
            }

            var response = await htc.GetAsync(loadUrl);

            if (!response.IsSuccessStatusCode)
            {
                response = await htc.GetAsync(loadUrl + ".atom"); // hack!
                if (!response.IsSuccessStatusCode) return null;
            }
            var converters = new List<IConverterFactory> { new AS2ConverterFactory(), new AtomConverterFactory(false) };
            tryAgain:
            ASObject data = null;
            foreach (var converter in converters)
            {
                if (converter.CanParse && ConverterHelpers.GetBestMatch(converter.MimeTypes, response.Content.Headers.ContentType.ToString()) != null)
                {
                    data = await converter.Build(_serviceProvider, null).Parse(await response.Content.ReadAsStreamAsync());
                    break;
                }
            }

            if (data == null)
            {
                if (response.Headers.Contains("Link"))
                {
                    var split = string.Join(", ", response.Headers.GetValues("Link")).Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var spl in split)
                    {
                        var args = spl.Split(';').Select(a => a.Trim()).ToArray();
                        if (args.Contains("rel=\"alternate\"") && args.Contains("type=\"application/atom+xml\""))
                        {
                            response = await htc.GetAsync(args[0].Substring(1, args[0].Length - 2));
                            goto tryAgain;
                        }
                    }
                }

                try
                {
                    var links = (await response.Content.ReadAsStringAsync()).Split('\n');
                    var alt = links.FirstOrDefault(a => a.Contains("application/atom+xml") && a.Contains("alternate"));
                    if (alt == null) return null;
                    var l = XDocument.Parse(alt + "</link>").Root;
                        if (l.Attribute(XNamespace.None + "type")?.Value == "application/atom+xml")
                        {
                            response = await htc.GetAsync(l.Attribute(XNamespace.None + "href")?.Value);
                            goto tryAgain;
                        }
                }
                catch (Exception) { }

                return null;
            }

            // forces out the old lazy load, if used
            await _entityFlattener.FlattenAndStore(Next, data, false);
            await Next.CommitChanges();

            return await Next.GetEntity(id, true);
        }

        public async Task<APEntity> StoreEntity(APEntity entity) => Next == null ? entity : await Next.StoreEntity(entity);

        public async Task CommitChanges()
        {
            if (Next != null) await Next.CommitChanges();
        }
    }
}
