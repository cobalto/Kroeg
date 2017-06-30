using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Salmon;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Kroeg.Server.Middleware.Renderers
{
    public class SalmonConverterFactory : IConverterFactory
    {
        public bool CanParse => true;
        public string FileExtension => "salmon";
        public bool CanRender => true;

        public List<string> MimeTypes => new List<string> { "application/magic-envelope+xml" };

        public string RenderMimeType => MimeTypes[0];

        public IConverter Build(IServiceProvider serviceProvider)
        {
            return ActivatorUtilities.CreateInstance<SalmonConverter>(serviceProvider, this);
        }

        private class SalmonConverter : IConverter
        {
            private IEntityStore _entityStore;
            private EntityFlattener _flattener;
            private SalmonConverterFactory _factory;

            private AtomEntryParser _entryParser;
            private AtomEntryGenerator _entryGenerator;
            private APContext _context;

            public SalmonConverter(IEntityStore entityStore, EntityFlattener flattener, AtomEntryParser parser, AtomEntryGenerator generator, SalmonConverterFactory factory, APContext context)
            {
                _entityStore = entityStore;
                _flattener = flattener;
                _factory = factory;

                _entryParser = parser;
                _entryGenerator = generator;
                _context = context;
            }

            public async Task<ASObject> Parse(Stream request)
            {
                string data;
                using (var r = new StreamReader(request))
                    data = await r.ReadToEndAsync();

                var envelope = new MagicEnvelope(XDocument.Parse(data));
                var entry = await _entryParser.Parse(XDocument.Parse(envelope.RawData), true);

                return await _entryParser.Parse(XDocument.Parse(data), false);
            }

            public async Task Render(HttpRequest request, HttpResponse response, ASObject toRender)
            {
                response.ContentType = ConverterHelpers.GetBestMatch(_factory.MimeTypes, request.Headers["Accept"]);

                var user = await _entityStore.GetEntity((string) toRender["actor"].Single().Primitive, false);
                var key = await _context.GetKey(user.Id);
                var magicKey = key != null ? new MagicKey(key.PrivateKey) : MagicKey.Generate();

                var doc = await _entryGenerator.Build(toRender);
                var enveloped = new MagicEnvelope(doc.ToString(), "application/atom+xml", magicKey);
                await response.WriteAsync(enveloped.Build().ToString());
            }
        }
    }
}
