// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR
{
    public class ServiceHubLifetimeManager<THub> : ExHubLifetimeManager<THub>
    {
        private readonly string _hubName;
        private readonly HubConnectionList _connections = new HubConnectionList();
        private readonly HubGroupList _groups = new HubGroupList();
        private ILogger<ServiceHubLifetimeManager<THub>> _logger;

        public ServiceHubLifetimeManager() : this(string.Empty, null)
        {
        }

        public ServiceHubLifetimeManager(string hubName, ILogger<ServiceHubLifetimeManager<THub>> logger)
        {
            _hubName = hubName;
            _logger = logger ?? NullLogger<ServiceHubLifetimeManager<THub>>.Instance;
        }

        public override Task AddGroupAsync(string connectionId, string groupName)
        {
            CheckNullConnectionId(connectionId);
            CheckNullGroupName(groupName);

            var connection = _connections[connectionId];
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            _groups.Add(connection, groupName);

            return Task.CompletedTask;
        }

        public override Task RemoveGroupAsync(string connectionId, string groupName)
        {
            CheckNullConnectionId(connectionId);
            CheckNullGroupName(groupName);

            var connection = _connections[connectionId];
            if (connection == null)
            {
                return Task.CompletedTask;
            }

            _groups.Remove(connectionId, groupName);

            return Task.CompletedTask;
        }

        public override Task InvokeAllAsync(string methodName, object[] args)
        {
            return InvokeAllWhere(methodName, args, c => true);
        }

        private Task InvokeAllWhere(string methodName, object[] args, Func<HubConnectionContext, bool> include)
        {
            var count = _connections.Count;
            if (count == 0)
            {
                return Task.CompletedTask;
            }

            var tasks = new List<Task>(count);
            var message = CreateInvocationMessage(methodName, args);

            // TODO: serialize once per format by providing a different stream?
            foreach (var connection in _connections)
            {
                if (!include(connection))
                {
                    continue;
                }

                tasks.Add(connection.WriteAsync(message));
            }

            return Task.WhenAll(tasks);
        }

        public override Task InvokeConnectionAsync(string connectionId, string methodName, object[] args)
        {
            CheckNullConnectionId(connectionId);

            var connection = _connections[connectionId];

            if (connection == null)
            {
                return Task.CompletedTask;
            }

            var message = CreateInvocationMessage(methodName, args);

            return connection.WriteAsync(message);
        }

        public override Task InvokeGroupAsync(string groupName, string methodName, object[] args)
        {
            CheckNullGroupName(groupName);

            var group = _groups[groupName];
            if (group == null) return Task.CompletedTask;

            var message = CreateInvocationMessage(methodName, args);
            var tasks = group.Values.Select(c => c.WriteAsync(message));
            return Task.WhenAll(tasks);
        }

        public override Task InvokeGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object[] args)
        {
            // Each task represents the list of tasks for each of the writes within a group
            var tasks = new List<Task>();
            var message = CreateInvocationMessage(methodName, args);

            foreach (var groupName in groupNames)
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    throw new ArgumentException(nameof(groupName));
                }

                var group = _groups[groupName];
                if (group != null)
                {
                    tasks.Add(Task.WhenAll(group.Values.Select(c => c.WriteAsync(message))));
                }
            }

            return Task.WhenAll(tasks);
        }

        public override Task InvokeGroupExceptAsync(string groupName, string methodName, object[] args, IReadOnlyList<string> excludedIds)
        {
            CheckNullGroupName(groupName);

            var group = _groups[groupName];
            if (group == null) return Task.CompletedTask;

            var message = CreateInvocationMessage(methodName, args);
            var tasks = group.Values.Where(connection => !excludedIds.Contains(connection.ConnectionId))
                .Select(c => c.WriteAsync(message));
            return Task.WhenAll(tasks);
        }

        private InvocationMessage CreateInvocationMessage(string methodName, object[] args)
        {
            return new InvocationMessage(target: methodName, argumentBindingException: null, arguments: args);
        }

        public override Task InvokeUserAsync(string userId, string methodName, object[] args)
        {
            return InvokeAllWhere(methodName, args, connection =>
                string.Equals(connection.UserIdentifier, userId, StringComparison.Ordinal));
        }

        public override Task OnConnectedAsync(HubConnectionContext connection)
        {
            _connections.Add(connection);
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(HubConnectionContext connection)
        {
            _connections.Remove(connection);
            _groups.RemoveDisconnectedConnection(connection.ConnectionId);
            return Task.CompletedTask;
        }

        // This method is only applicable to client-side HubLifetimeManager.
        // It is called when a server-side connection is closed, so all "connected" client connections can be closed accordingly.
        public override Task OnServerDisconnectedAsync(string serverConnectionId)
        {
            foreach (var connection in _connections)
            {
                if (!connection.TryGetRouteTarget(out var target) || !string.Equals(serverConnectionId,
                        target.ConnectionId, StringComparison.InvariantCultureIgnoreCase)) continue;

                _logger.LogInformation(
                    $"Disconnect client connection [{connection.ConnectionId}] because server connection [{serverConnectionId}] is disconnected.");
                connection.Abort();
            }
            return Task.CompletedTask;
        }

        public override Task InvokeAllExceptAsync(string methodName, object[] args, IReadOnlyList<string> excludedIds)
        {
            return InvokeAllWhere(methodName, args, connection => !excludedIds.Contains(connection.ConnectionId));
        }

        public override Task InvokeConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object[] args)
        {
            return InvokeAllWhere(methodName, args, connection => connectionIds.Contains(connection.ConnectionId));
        }

        public override Task InvokeUsersAsync(IReadOnlyList<string> userIds, string methodName, object[] args)
        {
            return InvokeAllWhere(methodName, args, connection => userIds.Contains(connection.UserIdentifier));
        }

        public override Task InvokeConnectionAsync(string connectionId, HubMethodInvocationMessage message)
        {
            return InternalInvokeConnectionAsync(connectionId, message);
        }

        public override Task InvokeConnectionAsync(string connectionId, CompletionMessage message)
        {
            return InternalInvokeConnectionAsync(connectionId, message);
        }

        private Task InternalInvokeConnectionAsync(string connectionId, HubInvocationMessage message)
        {
            CheckNullConnectionId(connectionId);

            var connection = _connections[connectionId];

            return connection?.WriteAsync(message) ?? Task.CompletedTask;
        }

        private void CheckNullConnectionId(string connectionId)
        {
            if (connectionId == null)
            {
                throw new ArgumentNullException(nameof(connectionId));
            }
        }

        private void CheckNullGroupName(string groupName)
        {
            if (groupName == null)
            {
                throw new ArgumentNullException(nameof(groupName));
            }
        }
    }
}
