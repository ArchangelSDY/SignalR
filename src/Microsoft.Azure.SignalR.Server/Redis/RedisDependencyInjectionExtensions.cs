// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR;
using Microsoft.Azure.SignalR.Redis;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RedisDependencyInjectionExtensions
    {
        public static ISignalRBuilder AddRedis(this ISignalRBuilder builder)
        {
            return AddRedis(builder, o => { });
        }

        public static ISignalRBuilder AddRedis(this ISignalRBuilder builder, Action<RedisOptions> configure)
        {
            builder.Services.Configure(configure);
            builder.Services.AddSingleton(typeof(IHubLifetimeManagerFactory), typeof(RedisHubLifetimeManagerFactory));
            builder.Services.AddSingleton(typeof(IHubConnectionRouter), typeof(RedisHubConnectionRouter));
            return builder;
        }
    }
}