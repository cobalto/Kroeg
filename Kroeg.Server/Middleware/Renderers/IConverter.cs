using Kroeg.ActivityStreams;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private static _mime Normalize(string mime)
        {
            var result = new _mime();
            var spl = mime.Split(';').Select(a => a.Trim()).ToArray();
            result.Type = spl[0];
            foreach (var line in spl.Skip(1))
            {
                var linesplit = line.Split(new char[] { '=' }, 2);
                if (linesplit.Length == 1)
                    result.Values[linesplit[0]] = "";
                else
                {
                    var val = linesplit[1];
                    if (val.StartsWith("\"") && val.EndsWith("\""))
                        val = val.Substring(1, val.Length - 2);

                    result.Values[linesplit[0]] = val;
                }
            }

            return result;
        }

        private class _mime
        {
            public string Type { get; set; }
            public Dictionary<string, string> Values { get; } = new Dictionary<string, string>();

            public override string ToString()
            {
                if (Values.Count == 0) return Type;
                return Type + "; " + string.Join("; ", Values.Select(a => a.Value == "" ? a.Key : $"{a.Key}=\"{a.Value}\""));
            }
        }

        private static bool _equal(string mimetype, _mime mold)
        {
            var made = Normalize(mimetype.Trim());
            bool isEqual = made.Type == mold.Type;
            foreach (var val in mold.Values)
            {
                isEqual = isEqual && made.Values.ContainsKey(val.Key) && made.Values[val.Key] == val.Value;
            }

            return isEqual;
        }

        public static string GetBestMatch(List<string> mimeTypes, StringValues stringValues)
        {
            var requestMime = string.Join(", ", stringValues).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(a => Normalize(a.Trim()));
            return requestMime.FirstOrDefault(a => mimeTypes.Any(b => _equal(b, a)))?.ToString();
        }
    }
}
