// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;

namespace Microsoft.Azure.SignalR
{
    public class ServerHubInvoker : IHubInvoker<ServerHub>
    {
        private readonly IHubMessageBroker _hubMessageBroker;
        private readonly IHubStatusManager _hubStatusManager;

        private Func<HubConnectionContext, string> GetHubName =
            connection => connection.GetHubName() ?? throw new Exception($"No specified hub binded to connection: {connection.ConnectionId}");

        public ServerHubInvoker(IHubMessageBroker hubMessageBroker, IHubStatusManager hubStatusManager)
        {
            _hubMessageBroker = hubMessageBroker;
            _hubStatusManager = hubStatusManager;
        }

        public Type GetReturnType(string invocationId)
        {
            throw new NotImplementedException();
        }

        public Type[] GetParameterTypes(string methodName)
        {
            throw new NotImplementedException();
        }

        public async Task OnConnectedAsync(HubConnectionContext connection)
        {
            var hubName = GetHubName(connection);
            await _hubMessageBroker.OnServerConnectedAsync(hubName, connection);
            _ = _hubStatusManager.AddServerConnection(hubName);
            _ = _hubStatusManager.AddOperation();
        }

        public async Task OnDisconnectedAsync(HubConnectionContext connection, Exception exception)
        {
            var hubName = GetHubName(connection);
            await _hubMessageBroker.OnServerDisconnectedAsync(hubName, connection);
            _ = _hubStatusManager.RemoveServerConnection(hubName);
            _ = _hubStatusManager.AddOperation();
        }

        public async Task OnInvocationAsync(HubConnectionContext connection, HubMethodInvocationMessage message, bool isStreamedInvocation)
        {
            var hubName = GetHubName(connection);
            await _hubMessageBroker.PassThruServerMessage(hubName, connection, message);
            _ = _hubStatusManager.AddServerMessage(hubName);
            _ = _hubStatusManager.AddOperation();
        }

        public async Task OnCompletionAsync(HubConnectionContext connection, CompletionMessage message)
        {
            var hubName = GetHubName(connection);
            await _hubMessageBroker.PassThruServerMessage(hubName, connection, message);
        }
    }
}
