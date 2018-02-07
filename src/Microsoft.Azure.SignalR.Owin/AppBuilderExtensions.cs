// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.SignalR;

namespace Owin
{
    public static class AzureSignalRAppBuilderExtensions
    {
        public static IAppBuilder UseAzureSignalR(this IAppBuilder app, string connectionString,
            Action<HubServerBuilder> configure)
        {
            var signalr = SignalRService.CreateFromConnectionString(connectionString);
            var builder = new HubServerBuilder(SignalRService.ServiceProvider, signalr);
            configure(builder);

            return app;
        }
    }
}
