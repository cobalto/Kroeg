using Kroeg.ActivityStreams;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Middleware.Renderers
{
    public interface IConverterFactory
    {
        bool CanParse { get; }
        bool CanRender { get; }


        string FileExtension { get; }
        List<string> MimeTypes { get; }
        string RenderMimeType { get; }

        IConverter Build(IServiceProvider serviceProvider, string user);
    }

    public interface IConverter
    {
        Task<ASObject> Parse(Stream data);
        Task Render(HttpRequest request, HttpResponse response, ASObject toRender);
    }

    public static class ConverterHelpers
    {
        public static string GetBestMatch(List<string> mimeTypes, StringValues stringValues)
        {
            var requestMime = string.Join(", ", stringValues).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim());
            return requestMime.FirstOrDefault(a => mimeTypes.Contains(a));
        }
    }
}
