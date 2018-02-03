// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ServiceSample
{
    internal static class DependencyInjectionExtensions
    {
        public static ISignalRBuilder AddRedis(this ISignalRBuilder builder, string connectionString)
        {
            return string.IsNullOrEmpty(connectionString)
                ? builder
                : builder.AddRedis(options => options.Options = ConfigurationOptions.Parse(connectionString));
        }
    }
}
