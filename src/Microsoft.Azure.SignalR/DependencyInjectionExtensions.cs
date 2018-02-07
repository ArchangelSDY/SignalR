// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Core;
using Microsoft.Azure.SignalR;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class AzureSignalRDependencyInjectionExtensions
    {
        public static IServiceCollection AddAzureSignalR(this IServiceCollection services, Action<HubOptions> configureHub = null)
        {
            if (configureHub != null) services.Configure(configureHub);
            
            services.AddSingleton(typeof(HubLifetimeManager<>), typeof(ServiceHubLifetimeManager<>));
            services.AddSingleton(typeof(IHubContext<>), typeof(HubContext<>));
            services.AddSingleton(typeof(IUserIdProvider), typeof(DefaultUserIdProvider));
            services.AddTransient(typeof(IHubActivator<>), typeof(DefaultHubActivator<>));

            services.AddSingleton(typeof(IHubInvoker<>), typeof(ServiceHubInvoker<>));
            services.AddSingleton(typeof(HubServer<>));

            services.AddAuthorization();

            return services;
        }
    }
}
