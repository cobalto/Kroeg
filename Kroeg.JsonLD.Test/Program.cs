using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Kroeg.JsonLD;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Kroeg.JsonLD.Test.Test
{
    class Program
    {
        private static async Task<JObject> Resolve(string uri)
        {
            var hc = new HttpClient();
            hc.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json, application/ld+json");

            var data = await hc.GetStringAsync(uri);
            return JObject.Parse(data);
        }

        private static async Task<int> do2()
        {
            var data = File.ReadAllText("data.json");
            var a = new API(Resolve);
            var result = await a.Expand(JObject.Parse(data));
            Console.WriteLine(((JObject) result).ToString(Formatting.Indented));

            return 1;
        }

        static void Main(string[] args)
        {
            Console.WriteLine(do2().Result);
            Console.ReadLine();
        }
    }
}
