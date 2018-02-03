using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceSample
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.AddSignalRServer(configureServer: options =>
            {
                options.AudienceProvider = hubName => new[]
                {
                    $"{Configuration["Auth:JWT:Audience"]}/client/?hub={hubName}",
                    $"{Configuration["Auth:JWT:Audience"]}/server/?hub={hubName}"
                };
                options.SigningKeyProvider = () => new[]
                {
                    Configuration["Auth:JWT:IssuerSigningKey"],
                    Configuration["Auth:JWT:IssuerSigningKey2"]
                };
            }).AddRedis(Configuration["Redis:ConnectionString"]);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseMvc();
            app.UseSignalRServer();
        }
    }
}
