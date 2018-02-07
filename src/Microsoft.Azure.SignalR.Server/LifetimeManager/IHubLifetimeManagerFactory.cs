// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Azure.SignalR
{
    public interface IHubLifetimeManagerFactory
    {
        ExHubLifetimeManager<THub> Create<THub>(string hubName) where THub : Hub;
    }
}
