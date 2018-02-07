// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Azure.SignalR
{
    public class ServiceHubLifetimeManager<THub> : HubLifetimeManager<THub>
    {
        private long _nextInvocationId;
        private readonly HubConnectionList _connections = new HubConnectionList();
        private readonly HubGroupList _groups = new HubGroupList();

        public override Task InvokeUsersAsync(IReadOnlyList<string> userIds, string methodName, object[] args)
        {
            throw new NotImplementedException();
        }

        public override Task AddGroupAsync(string connectionId, string groupName)
        {
            if (connectionId == null)
            {
                throw new ArgumentNullException(nameof(connectionId));
            }

            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            var connection = _connections[connectionId];
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            _groups.Add(connection, groupName);
            // Ask SignalR Service to do 'AddGroupAsync'
            var message = CreateInvocationMessage(nameof(AddGroupAsync), new object[0],
                action: nameof(AddGroupAsync),
                connectionId: connectionId,
                groupName: groupName);
            return connection.WriteAsync(message);
        }

        public override Task InvokeAllAsync(string methodName, object[] args)
        {
            // Send the message to SignalR Service through any existing connection.
            // Here, we choose the first connection.
            var connection = _connections.ElementAt(0);
            var message = CreateInvocationMessage(methodName, args, action: nameof(InvokeAllAsync));
            return connection.WriteAsync(message);
        }

        public override Task InvokeAllExceptAsync(string methodName, object[] args, IReadOnlyList<string> excludedIds)
        {
            var connection = _connections.ElementAt(0);
            var message = CreateInvocationMessage(methodName, args,
                action: nameof(InvokeConnectionAsync),
                excludedId: excludedIds);
            return connection.WriteAsync(message);
        }

        public override Task InvokeConnectionAsync(string connectionId, string methodName, object[] args)
        {
            if (connectionId == null)
            {
                throw new ArgumentNullException(nameof(connectionId));
            }

            var connection = _connections[connectionId];

            if (connection == null)
            {
                return Task.CompletedTask;
            }
            var message = CreateInvocationMessage(methodName, args,
                action: nameof(InvokeConnectionAsync),
                connectionId: connectionId);
            return connection.WriteAsync(message);
        }

        public override Task InvokeConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object[] args)
        {
            throw new NotImplementedException();
        }

        public override Task InvokeGroupAsync(string groupName, string methodName, object[] args)
        {
            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            var group = _groups[groupName];
            if (group == null) return Task.CompletedTask;

            var connection = group.Values.ElementAt(0);
            var message = CreateInvocationMessage(methodName, args,
                action: nameof(InvokeGroupAsync),
                groupName: groupName);
            return connection.WriteAsync(message);
        }

        public override Task InvokeGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object[] args)
        {
            throw new NotImplementedException();
        }

        public override Task InvokeGroupExceptAsync(string groupName, string methodName, object[] args, IReadOnlyList<string> excludedIds)
        {
            throw new NotImplementedException();
        }

        public override Task InvokeUserAsync(string userId, string methodName, object[] args)
        {
            throw new NotImplementedException();
        }

        public override Task OnConnectedAsync(HubConnectionContext connection)
        {
            _connections.Add(connection);
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(HubConnectionContext connection)
        {
            _connections.Remove(connection);
            return Task.CompletedTask;
        }

        public override Task RemoveGroupAsync(string connectionId, string groupName)
        {
            if (connectionId == null)
            {
                throw new ArgumentNullException(nameof(connectionId));
            }

            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }

            var connection = _connections[connectionId];
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            _groups.Remove(connectionId, groupName);
            // Ask SignalR Service to do 'RemoveGroupAsync'
            var message = CreateInvocationMessage(nameof(RemoveGroupAsync), new object[0],
                action: nameof(RemoveGroupAsync),
                connectionId: connectionId,
                groupName: groupName);
            return connection.WriteAsync(message);
        }

        private string GetInvocationId()
        {
            return Interlocked.Increment(ref _nextInvocationId).ToString();
        }

        private InvocationMessage CreateInvocationMessage(string methodName, object[] args, string action = null,
            string connectionId = null, string groupName = null, IReadOnlyList<string> excludedId = null)
        {
            var message = new InvocationMessage(GetInvocationId(), methodName, null, args);
            if (!string.IsNullOrEmpty(action))
            {
                message.AddAction(action);
            }
            if (!string.IsNullOrEmpty(connectionId))
            {
                message.AddConnectionId(connectionId);
            }
            if (!string.IsNullOrEmpty(groupName))
            {
                message.AddGroupName(groupName);
            }
            if (excludedId != null && excludedId.Any())
            {
                message.AddExcludedIds(excludedId);
            }
            return message;
        }
    }
}
