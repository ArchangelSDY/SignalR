// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.SignalR;

namespace Microsoft.AspNetCore.Builder
{
    public static class AzureSignalRApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseAzureSignalR(this IApplicationBuilder app,
            string connectionString, Action<HubServerBuilder> configure)
        {
            // Assign only once
            SignalRService.ServiceProvider = app.ApplicationServices;

            var signalr = SignalRService.CreateFromConnectionString(connectionString);
            var builder = new HubServerBuilder(app.ApplicationServices, signalr);
            configure(builder);

            return app;
        }
    }
}
