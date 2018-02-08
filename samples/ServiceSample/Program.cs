using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace ServiceSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            new WebHostBuilder()
                .UseSetting(WebHostDefaults.PreventHostingStartupKey, "true")
                .ConfigureLogging(LoggingConfig)
                .UseKestrel()
                .UseUrls("http://*:5001/", "http://*:5002/")
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .Build()
                .Run();
        }

        private static readonly Action<WebHostBuilderContext, ILoggingBuilder> LoggingConfig =
            (context, builder) =>
            {
                builder.AddConfiguration(context.Configuration.GetSection("Logging"));
                builder.AddConsole();
                builder.AddDebug();
            };
    }
}
