using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.SignalR
{
    public class SignalR
    {
        private const string HostNameProperty = "hostname";
        private const string AccessKeyProperty = "accesskey";

        private readonly ServiceOptions _options;

        public TimeSpan JwtBearerLifetime
        {
            get => _options.JwtBearerLifetime;
            set => _options.JwtBearerLifetime = value;
        }

        public int ConnectionNumber
        {
            get => _options.ConnectionNumber;
            set => _options.ConnectionNumber = value;
        }

        public string ApiVersion
        {
            get => _options.ApiVersion;
            set => _options.ApiVersion = value;
        }

        public string HostName { get; }

        public string AccessKey { get; }

        public SignalR(string hostName, string accessKey, ServiceOptions options = null)
        {
            HostName = hostName;
            AccessKey = accessKey;
            _options = options ?? new ServiceOptions();
        }

        #region Public Methods

        public string GetClientUrl<THub>() where THub : Hub
        {
            return GetClientUrl(typeof(THub).Name.ToLower());
        }

        public string GetClientUrl(string hubName)
        {
            // TODO: Use HTTPS
            return $"http://{HostName}/client/?hub={hubName}";
        }

        public string GetServerUrl<THub>() where THub : Hub
        {
            return GetServerUrl(typeof(THub).Name.ToLower());
        }

        public string GetServerUrl(string hubName)
        {
            // TODO: Use HTTPS
            return $"http://{HostName}/server/?hub={hubName}";
        }

        public string GenerateClientToken<THub>(IEnumerable<Claim> claims = null) where THub : Hub
        {
            return GenerateClientToken(typeof(THub).Name.ToLower(), claims);
        }

        public string GenerateClientToken(string hubName, IEnumerable<Claim> claims = null)
        {
            return AuthenticationHelper.GenerateJwtBearer(
                audience: $"{HostName}/client/?hub={hubName}",
                claims: claims,
                expires: DateTime.UtcNow.Add(JwtBearerLifetime),
                signingKey: AccessKey
            );
        }

        public string GenerateServerToken<THub>() where THub : Hub
        {
            return GenerateServerToken(typeof(THub).Name.ToLower());
        }

        public string GenerateServerToken(string hubName)
        {
            return AuthenticationHelper.GenerateJwtBearer(
                audience: $"{HostName}/server/?hub={hubName}",
                claims: null,
                expires: DateTime.UtcNow.Add(JwtBearerLifetime),
                signingKey: AccessKey
            );
        }

        public HubServer<THub> CreateHubServer<THub>() where THub : Hub
        {
            var hubServer = ServiceProvider.GetRequiredService<HubServer<THub>>();
            hubServer.UseService(this);
            return hubServer;
        }

        public HubProxy CreateHubProxy<THub>() where THub : Hub
        {
            return CreateHubProxy(typeof(THub).Name.ToLower());
        }

        public HubProxy CreateHubProxy(string hubName)
        {
            if (string.IsNullOrEmpty(hubName))
            {
                throw new ArgumentException(nameof(hubName));
            }

            return new HubProxy(this, hubName.ToLower());
        }

        #endregion

        #region Static Methods

        public static bool TryParse(string connectionString, out SignalR signalr)
        {
            signalr = null;
            if (string.IsNullOrEmpty(connectionString)) return false;

            var dict = connectionString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(new[] { '=' }, 2))
                .ToDictionary(t => t[0].Trim().ToLower(), t => t[1].Trim(), StringComparer.InvariantCultureIgnoreCase);
            if (!dict.ContainsKey(HostNameProperty) || !dict.ContainsKey(AccessKeyProperty)) return false;

            signalr = new SignalR(dict[HostNameProperty], dict[AccessKeyProperty]);
            return true;
        }

        public static SignalR Parse(string connectionString)
        {
            return TryParse(connectionString, out var signalr)
                ? signalr
                : throw new ArgumentException($"Invalid connection string: {connectionString}");
        }

        #endregion

        #region Static Properties

        private static IServiceProvider _externalServiceProvider = null;

        private static readonly Lazy<IServiceProvider> InternalServiceProvider =
            new Lazy<IServiceProvider>(
                () => new ServiceCollection()
                    .AddLogging()
                    .AddAuthorization()
                    .AddSingleton(typeof(HubLifetimeManager<>), typeof(ServiceHubLifetimeManager<>))
                    .AddSingleton(typeof(IHubContext<>), typeof(HubContext<>))
                    .AddSingleton(typeof(IHubInvoker<>), typeof(ServiceHubInvoker<>))
                    .AddTransient(typeof(IHubActivator<>), typeof(DefaultHubActivator<>))
                    .AddSingleton(typeof(HubServer<>))
                    .BuildServiceProvider());

        public static IServiceProvider ServiceProvider
        {
            get => _externalServiceProvider ?? InternalServiceProvider.Value;
            internal set => _externalServiceProvider = value;
        }

        #endregion
    }
}
