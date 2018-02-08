using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceSample
{
    public class Startup
    {
        public const string ServiceId = "ServiceId";
        public const string Audience = "Auth:JWT:Audience";
        public const string PrimarySigningKey = "Auth:JWT:IssuerSigningKey";
        public const string SecondarySigningKey = "Auth:JWT:IssuerSigningKey2";
        public const string RedisConnectionString = "Redis:ConnectionString";

        public Startup(IHostingEnvironment env, IConfiguration config)
        {
            HostingEnvironment = env;
            Configuration = config;
        }

        public IHostingEnvironment HostingEnvironment { get; }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.AddSignalRServer(options =>
            {
                ConfigureServiceId(options);
                ConfigureAudience(options);
                ConfigureSigningKeys(options);
            }).AddRedis(Configuration[RedisConnectionString]);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseMvc();
            app.UseSignalRServer();
        }

        #region Private Methods

        private void ConfigureServiceId(ServerOptions options)
        {
            if (string.IsNullOrEmpty(Configuration[ServiceId])) return;
            options.ServiceId = Configuration[ServiceId];
        }

        private void ConfigureAudience(ServerOptions options)
        {
            if (string.IsNullOrEmpty(Configuration[Audience])) return;
            options.AudienceProvider = hubName => new[]
            {
                $"{Configuration[Audience]}/client/?hub={hubName}",
                $"{Configuration[Audience]}/server/?hub={hubName}",
            };
        }

        private void ConfigureSigningKeys(ServerOptions options)
        {
            if (string.IsNullOrEmpty(Configuration[PrimarySigningKey]) &&
                string.IsNullOrEmpty(Configuration[SecondarySigningKey])) return;

            options.SigningKeyProvider = () => new[]
            {
                Configuration[PrimarySigningKey],
                Configuration[SecondarySigningKey]
            };
        }

        #endregion
    }
}
