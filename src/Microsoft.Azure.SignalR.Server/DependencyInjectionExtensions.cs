// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Core;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.SignalR
{
    public static class DependencyInjectionExtensions
    {
        public static ISignalRBuilder AddSignalRServer(this IServiceCollection services,
            Action<ServerOptions> configureServer = null, Action<HubOptions> configureHub = null)
        {
            if (configureServer != null) services.Configure(configureServer);
            if (configureHub != null) services.Configure(configureHub);

            services.AddHttpContextAccessor();
            services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer();

            services.AddDistributedMemoryCache();

            services.AddSockets();
            services.AddSingleton(typeof(HubLifetimeManager<>), typeof(ServiceHubLifetimeManager<>));
            services.AddSingleton(typeof(IHubProtocolResolver), typeof(DefaultHubProtocolResolver));
            services.AddSingleton(typeof(IHubContext<>), typeof(HubContext<>));
            services.AddSingleton(typeof(IHubContext<,>), typeof(HubContext<,>));
            services.AddSingleton(typeof(HubEndPoint<>), typeof(HubEndPoint<>));
            services.AddSingleton(typeof(IUserIdProvider), typeof(DefaultUserIdProvider));
            services.AddScoped(typeof(IHubActivator<>), typeof(DefaultHubActivator<>));

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHubProtocol, CustomJsonHubProtocol>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHubProtocol, CustomMessagePackHubProtocol>());
            services.AddSingleton(typeof(IHubInvoker<ClientHub>), typeof(ClientHubInvoker));
            services.AddSingleton(typeof(IHubInvoker<ServerHub>), typeof(ServerHubInvoker));
            services.AddSingleton(typeof(IHubLifetimeManagerFactory), typeof(HubLifetimeManagerFactory));
            services.AddSingleton(typeof(IHubMessageBroker), typeof(HubMessageBroker));
            services.AddSingleton(typeof(IHubConnectionRouter), typeof(HubConnectionRouter));
            services.AddSingleton(typeof(IRoutingCache), typeof(DistributedRoutingCache));
            services.AddSingleton(typeof(IHubStatusManager), typeof(DefaultHubStatusManager));

            services.AddAuthorization();

            return new SignalRBuilder(services);
        }
    }
}
