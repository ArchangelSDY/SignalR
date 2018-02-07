// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.Azure.SignalR
{
    public abstract class ExHubLifetimeManager<THub> : HubLifetimeManager<THub>
    {
        public abstract Task InvokeConnectionAsync(string connectionId, HubMethodInvocationMessage message);

        public abstract Task InvokeConnectionAsync(string connectionId, CompletionMessage message);

        // TODO: extract a separate interface since this method only applies to client-side HubLifetimeManager
        public abstract Task OnServerDisconnectedAsync(string serverConnectionId);
    }
}
