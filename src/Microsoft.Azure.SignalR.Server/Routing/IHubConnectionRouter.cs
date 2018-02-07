// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Azure.SignalR
{
    public interface IHubConnectionRouter
    {
        Task OnClientConnected(string hubName, HubConnectionContext connection);

        Task OnClientDisconnected(string hubName, HubConnectionContext connection);

        void OnServerConnected(string hubName, HubConnectionContext connection);

        void OnServerDisconnected(string hubName, HubConnectionContext connection);
    }
}
