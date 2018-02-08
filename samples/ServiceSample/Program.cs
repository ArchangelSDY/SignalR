using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;

namespace ServiceSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            new WebHostBuilder()
                .UseSetting(WebHostDefaults.PreventHostingStartupKey, "true")
                .ConfigureLogging(LoggingConfig)
                .ConfigureAppConfiguration(ConfigurationConfig)
                .UseKestrel(KestrelConfig)
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

        private static readonly Action<WebHostBuilderContext, IConfigurationBuilder> ConfigurationConfig =
            (context, builder) =>
            {
                builder
                    .SetBasePath(context.HostingEnvironment.ContentRootPath)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                    .AddEnvironmentVariables();
            };

        private static readonly Action<WebHostBuilderContext, KestrelServerOptions> KestrelConfig =
            (context, options) =>
            {
                if ("true".Equals(context.Configuration["Https:Enabled"], StringComparison.OrdinalIgnoreCase))
                {
                    var cert = KeyVaultHelper.GetKestrelCertificate(context.Configuration);
                    options.Listen(IPAddress.Any, 5001, listenOptions => listenOptions.UseHttps(cert));
                    options.Listen(IPAddress.Any, 5002, listenOptions => listenOptions.UseHttps(cert));
                }
                else
                {
                    options.Listen(IPAddress.Any, 5001);
                    options.Listen(IPAddress.Any, 5002);
                }
            };
    }
}
