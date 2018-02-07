using System.Configuration;
using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(ServiceChatNetFxSample.Startup))]

namespace ServiceChatNetFxSample
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=316888
            app.UseAzureSignalR(ConfigurationManager.AppSettings["SignalRServiceConnectionString"],
                builder =>
                {
                    builder.UseHub<Chat>();
                });
        }
    }
}
