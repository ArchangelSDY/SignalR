// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.SignalR
{
    public class HubConnectionRouter : IHubConnectionRouter
    {
        /* Save connection number of each server
         * Key: Hub Name
         * Value: 
         *     Key: Server Id
         *     Value: Total connected clients to this server
         */
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _serverStatus =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();

        /* Save connection number of each connection from server
         * Key: {Hub Name}:{Server Id}
         * Value: 
         *     Key: Connection Id (from app server)
         *     Value: Total connected clients to this connection
         */
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _connectionStatus =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();

        private readonly object _lock = new Object();

        private readonly IRoutingCache _cache;
        private readonly ILogger<HubConnectionRouter> _logger;

        // TODO: inject dependency of config provider, so that we can change routing algorithm without restarting service
        public HubConnectionRouter(IRoutingCache cache, ILogger<HubConnectionRouter> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        #region Client Connect/Disconnect

        // TODO: Using least connection routing right now. Should support multiple routing method in the future.
        public async Task OnClientConnected(string hubName, HubConnectionContext connection)
        {
            if (connection.TryGetRouteTarget(out var target))
            {
                await _cache.SetTargetAsync(connection, target);
                return;
            }

            target = ResolveRoutingTarget(hubName, connection);

            if (target == null)
            {
                _logger.LogInformation("Found no route target for Client {0}",
                    connection.TryGetUid(out var uid) ? uid : connection.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Routing Client {0} to {1}",
                    connection.TryGetUid(out var uid) ? uid : connection.ConnectionId, target);

                connection.AddRouteTarget(target);

                await _cache.SetTargetAsync(connection, target);

                UpdateConnectionCount(hubName, target, c => c + 1);
            }
        }

        public async Task OnClientDisconnected(string hubName, HubConnectionContext connection)
        {
            if (!connection.TryGetRouteTarget(out var target))
            {
                await _cache.RemoveTargetAsync(connection);
                return;
            }

            await _cache.DelayRemoveTargetAsync(connection, target);

            UpdateConnectionCount(hubName, target, c => c > 0 ? c - 1 : 0);
        }

        #endregion

        #region Server Connect/Disconnect

        public void OnServerConnected(string hubName, HubConnectionContext connection)
        {
            var connectionId = connection.ConnectionId;
            if (!connection.TryGetUid(out var serverId))
            {
                serverId = connectionId;
            }

            ConcurrentDictionary<string, int> serverStatus;
            ConcurrentDictionary<string, int> connectionStatus;

            lock (_lock)
            {
                serverStatus = _serverStatus.GetOrAdd(hubName, _ => new ConcurrentDictionary<string, int>());
                connectionStatus =
                    _connectionStatus.GetOrAdd($"{hubName}:{serverId}", _ => new ConcurrentDictionary<string, int>());
            }

            serverStatus?.TryAdd(serverId, 0);
            // Add new entry for this connection
            connectionStatus?.TryAdd(connectionId, 0);
        }

        public void OnServerDisconnected(string hubName, HubConnectionContext connection)
        {
            var connectionId = connection.ConnectionId;
            if (!connection.TryGetUid(out var serverId))
            {
                serverId = connectionId;
            }

            lock (_lock)
            {
                if (!_connectionStatus.TryGetValue($"{hubName}:{serverId}", out var connectionStatus)) return;

                // Remove entry of this connection
                connectionStatus.TryRemove(connectionId, out _);

                if (connectionStatus.Count != 0) return;

                // No connections from server. Remove invalid server.
                _connectionStatus.TryRemove($"{hubName}:{serverId}", out _);
                if (_serverStatus.TryGetValue(hubName, out var serverStatus))
                {
                    serverStatus.TryRemove(serverId, out _);
                }
            }
        }

        #endregion

        #region Private Methods

        private void UpdateConnectionCount(string hubName, RouteTarget target, Func<int, int> updateFactory)
        {
            if (_serverStatus.TryGetValue(hubName, out var serverStatus))
            {
                serverStatus.TryUpdate(target.ServerId, updateFactory);
            }

            if (_connectionStatus.TryGetValue($"{hubName}:{target.ServerId}", out var connectionStatus))
            {
                connectionStatus.TryUpdate(target.ConnectionId, updateFactory);
            }
        }

        private RouteTarget ResolveRoutingTarget(string hubName, HubConnectionContext connection)
        {
            string targetServer = null;
            // Sticky session enabled and cached before
            if (_cache.TryGetTarget(connection, out var target))
            {
                // Route to connections from the same server
                targetServer = target.ServerId;
            }

            // Use lock to prevent race condition because we are modifying connection stats
            // TODO: use fine-grained lock
            lock (_lock)
            {
                if (!_serverStatus.TryGetValue(hubName, out var serverStatus) || serverStatus.IsEmpty) return null;

                while (!serverStatus.IsEmpty)
                {
                    // Find a server which has least connection if target server doesn't exist
                    if (string.IsNullOrEmpty(targetServer) || !serverStatus.ContainsKey(targetServer))
                    {
                        targetServer = serverStatus.Aggregate((l, r) => l.Value < r.Value ? l : r).Key;
                    }

                    var serverKey = $"{hubName}:{targetServer}";
                    if (_connectionStatus.TryGetValue(serverKey, out var connectionStatus))
                    {
                        if (connectionStatus.Count != 0)
                        {
                            var targetConnection = connectionStatus.Aggregate((l, r) => l.Value < r.Value ? l : r).Key;
                            return new RouteTarget
                            {
                                ServerId = targetServer,
                                ConnectionId = targetConnection
                            };
                        }

                        // No connections from target server. Remove invalid server.
                        _connectionStatus.TryRemove(serverKey, out _);
                        serverStatus.TryRemove(targetServer, out _);
                    }
                    else
                    {
                        // Remove invalid server
                        serverStatus.TryRemove(targetServer, out _);
                    }
                    targetServer = null;
                }
            }

            return null;
        }

        #endregion
    }
}
