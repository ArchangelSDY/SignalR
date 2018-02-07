// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Azure.SignalR
{
    public interface IRoutingCache
    {
        bool TryGetTarget(HubConnectionContext connection, out RouteTarget targetConnId);

        Task SetTargetAsync(HubConnectionContext connection, RouteTarget targetConnId);

        Task RemoveTargetAsync(HubConnectionContext connection);

        Task DelayRemoveTargetAsync(HubConnectionContext connection, RouteTarget targetConnId);
    }
}
