// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.AspNetCore.SignalR
{
    public abstract class ExHubLifetimeManager<THub> : HubLifetimeManager<THub>
    {
        public abstract Task InvokeConnectionAsync(string connectionId, HubMethodInvocationMessage message);

        public abstract Task InvokeConnectionAsync(string connectionId, CompletionMessage message);
    }
}
