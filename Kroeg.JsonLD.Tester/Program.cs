using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;

namespace Kroeg.JsonLD.Tester
{
    class Program
    {
    private static Dictionary<string, JObject> _objectStore = new Dictionary<string, JObject>();

        private static async Task<JObject> _resolve(string uri)
        {
            if (_objectStore.ContainsKey(uri)) return _objectStore[uri];

            var hc = new HttpClient();
            hc.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/ld+json"));

            return JObject.Parse(await hc.GetStringAsync(uri));
        }

        static async Task<int> _do()
        {
            var api = new API(_resolve);
//            var data = JObject.Parse(File.ReadAllText("test.json"));
            var data = await _resolve("https://mastodon.social/@Gargron.json");

            var expanded = await api.Expand(data);
            var context = await api.BuildContext(data["@context"]);
            var compacted= api.CompactExpanded(context, expanded);
            var flattened = api.Flatten(expanded, context);
            var rdf = api.MakeRDF(expanded);

            Console.WriteLine(expanded.ToString());
            Console.WriteLine(compacted.ToString());
            Console.WriteLine(flattened.ToString());
            Console.WriteLine(string.Join("\n", rdf["@default"].Select(a => a.ToString())));
            return 5;
        }

        static void Main(string[] args)
        {
            while (true) {
                Console.WriteLine(_do().Result);
                Console.ReadLine();
            }
        }
    }
}