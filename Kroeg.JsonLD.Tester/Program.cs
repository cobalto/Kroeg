using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kroeg.JsonLD.Tester
{
    class Program
    {
        private static async Task<JObject> _resolve(string uri)
        {
            var hc = new HttpClient();
            hc.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/ld+json"));

            return JObject.Parse(await hc.GetStringAsync(uri));
        }

        static async Task<int> _do()
        {
            var api = new API(_resolve);
            var data = JObject.Parse(File.ReadAllText("test.json"));

            var expanded = await api.Expand(data);
            Console.WriteLine(expanded.ToString());

            var context = await api.BuildContext("http://www.w3.org/ns/activitystreams");
            var compacted = api.CompactExpanded(context, expanded);

            Console.WriteLine(compacted.ToString());
            return 5;
        }

        static void Main(string[] args)
        {
            Console.WriteLine(_do().Result);
            Console.ReadLine();
        }
    }
}