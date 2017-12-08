// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Service.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder
{
    public static class SignalRServiceAppBuilderExtensions
    {
        public static IApplicationBuilder UseSignalRService(this IApplicationBuilder app, Action<ServiceHubBuilder> configHub)
        {
            configHub(new ServiceHubBuilder(app.ApplicationServices));
            return app;
        }
    }
}