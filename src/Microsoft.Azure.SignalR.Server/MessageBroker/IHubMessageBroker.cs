// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.Azure.SignalR
{
    public interface IHubMessageBroker
    {
        Task OnClientConnectedAsync(string hubName, HubConnectionContext context);

        Task OnClientDisconnectedAsync(string hubName, HubConnectionContext context);

        Task PassThruClientMessage(string hubName, HubConnectionContext connection, HubMethodInvocationMessage message);

        Task OnServerConnectedAsync(string hubName, HubConnectionContext context);

        Task OnServerDisconnectedAsync(string hubName, HubConnectionContext context);

        Task PassThruServerMessage(string hubName, HubConnectionContext context, HubMethodInvocationMessage message);

        Task PassThruServerMessage(string hubName, HubConnectionContext context, CompletionMessage message);

        HubLifetimeManager<ClientHub> GetHubLifetimeManager(string hubName);
    }
}
