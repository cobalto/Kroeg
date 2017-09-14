using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;

namespace Kroeg.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var listenOn = new Uri(args.Length == 0 ? "http://0.0.0.0:5000/" : args[0]);
            var builder = new UriBuilder(listenOn);
            var bur = config.GetSection("Kroeg");
            builder.Path = (new Uri(config.GetSection("Kroeg")["BaseUri"])).AbsolutePath ?? "/";

            var host = new WebHostBuilder()
                .UseKestrel()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseIISIntegration()
                .UseStartup<Startup>()
                .UseUrls(new string[] { builder.Uri.ToString() })
                .Build();

            host.Run();
        }
    }
}
