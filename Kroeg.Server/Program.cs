using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace Kroeg.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseIISIntegration()
                .UseStartup<Startup>()
                .UseUrls(args.Length == 0 ? new string[] {"http://0.0.0.0:5000/" } : args)
                .Build();

            host.Run();
        }
    }
}
