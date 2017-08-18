using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

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
            return 5;
        }

        static void Main(string[] args)
        {
            Console.WriteLine(_do().Result);
            Console.ReadLine();
        }
    }
}