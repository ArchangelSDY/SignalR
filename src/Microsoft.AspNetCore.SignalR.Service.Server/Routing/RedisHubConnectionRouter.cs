// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.AspNetCore.SignalR
{
    public class RedisHubConnectionRouter : IHubConnectionRouter, IDisposable
    {
        private const int ScanInterval = 3; //seconds

        private readonly IConnectionMultiplexer _redisConnection;
        private readonly IDatabase _redisDatabase;
        private readonly ILogger _logger;
        private readonly IRoutingCache _cache;
        private readonly string _serviceId;
        private readonly Timer _timer;

        private readonly object _lock = new object();
        private readonly object _syncLock = new object();

        /* Save connection number of each server (locally)
         * Key: Hub Name
         * Value: 
         *     Key: Server Id
         *     Value: Total connected clients to this server
         */
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _serverStatus =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();

        /* Save connection number of each connection from server (locally)
         * Key: {Hub Name}:{Server Id}
         * Value: 
         *     Key: Connection Id (from app server)
         *     Value: Total connected clients to this connection
         */
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _connectionStatus =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();

        // TODO: inject dependency of config provider, so that we can change routing algorithm without restarting service
        public RedisHubConnectionRouter(ILogger<RedisHubConnectionRouter> logger, IRoutingCache cache,
            IOptions<RedisOptions> redisOptions, IOptions<ServerOptions> serviceOptions)
        {
            _logger = logger;
            _cache = cache;
            _serviceId = serviceOptions.Value?.ServiceId ?? string.Empty;
            (_redisConnection, _redisDatabase) = ConnectToRedis(logger, redisOptions.Value);
            _timer = new Timer(Scan, this, TimeSpan.FromMilliseconds(0), TimeSpan.FromSeconds(ScanInterval));
        }

        public void Dispose()
        {
            _timer.Dispose();
            _redisConnection.Dispose();
        }

        #region Client Connect/Disconnect

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
                _logger.LogInformation("Client {0} routing to {1}",
                    connection.TryGetUid(out var uid) ? uid : connection.ConnectionId, target);

                connection.AddRouteTarget(target);

                // Update entry in Cache
                await _cache.SetTargetAsync(connection, target);

                // Update entry score in the Sorted Set in Redis
                await UpdateConnectionCount(hubName, target.ServerId, target.ConnectionId, 1);
            }
        }

        public async Task OnClientDisconnected(string hubName, HubConnectionContext connection)
        {
            if (!connection.TryGetRouteTarget(out var target))
            {
                await _cache.RemoveTargetAsync(connection);
                return;
            }

            // Update entry in Cache to expire in 30 minutes
            await _cache.DelayRemoveTargetAsync(connection, target);

            // Update entry score in the Sorted Set in Redis
            await UpdateConnectionCount(hubName, target.ServerId, target.ConnectionId, -1);
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
                connectionStatus = _connectionStatus.GetOrAdd($"{hubName}:{serverId}", _ => new ConcurrentDictionary<string, int>());
            }

            serverStatus?.TryAdd(serverId, 0);
            connectionStatus?.TryAdd(connectionId, 0);

            // Add new entry to a Sorted Set in Redis
            _redisDatabase.ScriptEvaluate(
                @"redis.call('zadd', KEYS[1], 0, KEYS[2])
                redis.call('zadd', KEYS[3], 0, KEYS[4])",
                new RedisKey[] {$"{_serviceId}:{hubName}", serverId, $"{_serviceId}:{hubName}:{serverId}", connectionId});
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
                if (_connectionStatus.TryGetValue($"{hubName}:{serverId}", out var connectionStatus))
                {
                    connectionStatus.TryRemove(connectionId, out _);
                    if (connectionStatus.IsEmpty)
                    {
                        _connectionStatus.TryRemove($"{hubName}:{serverId}", out _);
                        if (_serverStatus.TryGetValue(hubName, out var serverStatus))
                        {
                            serverStatus.TryRemove(serverId, out _);
                        }
                    }
                }
            }


            // Remove entry from Sorted Set in Redis
            // If no connections are from the same server, remove server from Redis
            _redisDatabase.ScriptEvaluate(
                @"redis.call('zrem', KEYS[1], KEYS[2])
                local conn_count = redis.call('zcard', KEYS[1])
                if conn_count == 0 then
                    redis.call('del', KEYS[1])
                    redis.call('zrem', KEYS[3], KEYS[4])
                end",
                new RedisKey[] { $"{_serviceId}:{hubName}:{serverId}", connectionId, $"{_serviceId}:{hubName}", serverId});
        }

        #endregion

        #region Misc

        private class LoggerTextWriter : TextWriter
        {
            private readonly ILogger _logger;

            public LoggerTextWriter(ILogger logger)
            {
                _logger = logger;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
            }

            public override void WriteLine(string value)
            {
                _logger.LogDebug(value);
            }
        }

        private (IConnectionMultiplexer, IDatabase) ConnectToRedis(ILogger logger, RedisOptions options)
        {
            var writer = new LoggerTextWriter(logger);
            var redisConnection = options.Connect(writer);
            redisConnection.ConnectionRestored += (_, e) =>
            {
                if (e.ConnectionType != ConnectionType.Subscription)
                {
                    _logger.ConnectionRestored();
                }
            };

            redisConnection.ConnectionFailed += (_, e) =>
            {
                if (e.ConnectionType != ConnectionType.Subscription)
                {
                    _logger.ConnectionFailed(e.Exception);
                }
            };

            if (redisConnection.IsConnected)
            {
                _logger.Connected();
            }
            else
            {
                _logger.NotConnected();
            }

            var database = redisConnection.GetDatabase(options.Options.DefaultDatabase.GetValueOrDefault());
            return (redisConnection, database);
        }

        private async Task UpdateConnectionCount(string hubName, string serverId, string connectionId, int value)
        {
            if (_serverStatus.TryGetValue(hubName, out var serverStatus))
            {
                serverStatus.TryUpdate(serverId, c => c + value);
            }

            if (_connectionStatus.TryGetValue($"{hubName}:{serverId}", out var connectionStatus))
            {
                connectionStatus.TryUpdate(connectionId, c => c + value);
            }

            await _redisDatabase.ScriptEvaluateAsync(
                @"if redis.call('zscore', KEYS[1], KEYS[2]) ~= nil then
                    redis.call('zincrby', KEYS[1], ARGV[1], KEYS[2])
                end
                if redis.call('zscore', KEYS[3], KEYS[4]) ~= nil then
                    redis.call('zincrby', KEYS[3], ARGV[1], KEYS[4])
                end",
                new RedisKey[] { $"{_serviceId}:{hubName}", serverId, $"{_serviceId}:{hubName}:{serverId}", connectionId },
                new RedisValue[] { value });
        }

        #endregion

        #region Routing

        private RouteTarget ResolveRoutingTarget(string hubName, HubConnectionContext connection)
        {
            string serverId = null;
            // Find existing client-server binding
            if (_cache.TryGetTarget(connection, out var target))
            {
                serverId = target.ServerId;
            }

            return ResolveRoutingTargetLocally(hubName, serverId) ?? ResolveRoutingTargetInRedis(hubName, serverId);
        }

        private RouteTarget ResolveRoutingTargetLocally(string hubName, string serverId)
        {
            lock (_lock)
            {
                // No connections from any app server are directly connected to this instance
                if (!_serverStatus.TryGetValue(hubName, out var serverStatus) || serverStatus.IsEmpty) return null;

                // Find a server which has least connection if no server id is specified
                if (string.IsNullOrEmpty(serverId))
                {
                    serverId = serverStatus.Aggregate((l, r) => l.Value < r.Value ? l : r).Key;
                }

                // No connection from target server are directly connected to this instance
                if (!serverStatus.ContainsKey(serverId)) return null;
                if (!_connectionStatus.TryGetValue($"{hubName}:{serverId}", out var connectionStatus) ||
                    connectionStatus.IsEmpty) return null;

                var targetConnection = connectionStatus.Aggregate((l, r) => l.Value < r.Value ? l : r).Key;
                return new RouteTarget
                {
                    ServerId = serverId,
                    ConnectionId = targetConnection
                };
            }
        }

        private RouteTarget ResolveRoutingTargetInRedis(string hubName, string serverId)
        {
            RedisValue[] results;
            if (string.IsNullOrEmpty(serverId))
            {
                // No target server.
                // Find the least connection server in Redis.
                _logger.LogInformation("Target server Id is null. Try to find the least connection server in Redis");
                results = (RedisValue[])_redisDatabase.ScriptEvaluate(
                    @"local ret = {}
                    local target_server, target_conn
                    if redis.call('zcard', KEYS[1]) ~= 0 then
                        target_server = redis.call('zrangebyscore', KEYS[1], '-inf', '+inf', 'LIMIT', '0', '1')
                        local server_key = KEYS[1] .. ':' .. target_server[1]
                        target_conn = redis.call('zrangebyscore', server_key, '-inf', '+inf', 'LIMIT', '0', '1')
                        table.insert(ret, target_conn)
                        table.insert(ret, target_server[1])
                    end
                    return ret", new RedisKey[] { $"{_serviceId}:{hubName}" });
            }
            else
            {
                _logger.LogInformation($"Target server Id is [{serverId}]. Try to find target server in Redis");
                results = (RedisValue[])_redisDatabase.ScriptEvaluate(
                    @"local ret = {}
                    local target_server, target_conn
                    if redis.call('zcard', KEYS[1]) ~= 0 then
                        target_conn = redis.call('zrangebyscore', KEYS[1], '-inf', '+inf', 'LIMIT', '0', '1')
                        table.insert(ret, target_conn)
                    elseif redis.call('zcard', KEYS[2]) ~= 0 then
                        target_server = redis.call('zrangebyscore', KEYS[2], '-inf', '+inf', 'LIMIT', '0', '1')
                        local server_key = KEYS[2] .. ':' .. target_server[1]
                        target_conn = redis.call('zrangebyscore', server_key, '-inf', '+inf', 'LIMIT', '0', '1')
                        table.insert(ret, target_conn)
                        table.insert(ret, target_server[1])
                    end
                    return ret", new RedisKey[] { $"{_serviceId}:{hubName}:{serverId}", $"{_serviceId}:{hubName}" });
            }
            return results.Any()
                    ? new RouteTarget
                    {
                        ConnectionId = results[0],
                        ServerId = results.Length > 1 ? (string)results[1] : serverId
                    }
                    : null;
        }

        #endregion

        #region Sync Status

        private static void Scan(object state)
        {
            _ = ((RedisHubConnectionRouter)state).Scan();
        }

        private async Task Scan()
        {
            if (!Monitor.TryEnter(_syncLock))
            {
                return;
            }

            try
            {
                _logger.LogInformation("Start syncing data from Redis");

                var tasks = _serverStatus.Keys.Select(SyncServerStatus);
                await Task.WhenAll(tasks);

                _logger.LogInformation("Completed syncing data from Redis");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while syncing data from Redis.");
            }
            finally
            {
                Monitor.Exit(_syncLock);
            }
        }

        private async Task SyncServerStatus(string hubName)
        {
            if (_serverStatus.TryGetValue(hubName, out var serverStatus))
            {
                _logger.LogInformation($"Start syncing data for hub [{hubName}]");
                var results = (RedisValue[]) await _redisDatabase.ScriptEvaluateAsync(
                    @"local ret = {}
                    for _, server in pairs(ARGV) do
                        table.insert(ret, server)
                        table.insert(ret, redis.call('zscore', KEYS[1], server))
                        for _, v in pairs(redis.call('zrangebyscore', KEYS[1]..':'..server, '-inf', '+inf', 'WITHSCORES')) do
                            table.insert(ret, v)
                        end
                    end
                    return ret
                    ",
                    new RedisKey[] {$"{_serviceId}:{hubName}"},
                    serverStatus.Select(s => (RedisValue) s.Key).ToArray());

                var dict = ConvertResultToDict(results);

                UpdateLocalStatus(hubName, dict);
            }
        }

        private IDictionary<string, int> ConvertResultToDict(RedisValue[] results)
        {
            if (results == null || results.Length < 2) return null;

            var dict = new Dictionary<string, int>();
            for (var i = 0; i < results.Length - 1; i += 2)
            {
                if (dict.ContainsKey(results[i])) continue;
                if (!int.TryParse(results[i + 1], out var value)) continue;
                dict.Add(results[i], value);
            }

            return dict;
        }

        private void UpdateLocalStatus(string hubName, IDictionary<string, int> statusFromRedis)
        {
            if (string.IsNullOrEmpty(hubName) || statusFromRedis == null) return;
            if (!_serverStatus.TryGetValue(hubName, out var serverStatus)) return;

            serverStatus.All(x =>
            {
                if (statusFromRedis.ContainsKey(x.Key))
                {
                    serverStatus.TryUpdate(x.Key, _ => statusFromRedis[x.Key]);
                }
                UpdateLocalConnectionStatus($"{hubName}:{x.Key}", statusFromRedis);
                return true;
            });
        }

        private void UpdateLocalConnectionStatus(string serverKey, IDictionary<string, int> statusFromRedis)
        {
            if (_connectionStatus.TryGetValue(serverKey, out var connectionStatus))
            {
                connectionStatus.Select(x =>
                    statusFromRedis.ContainsKey(x.Key) && connectionStatus.TryUpdate(x.Key, _ => statusFromRedis[x.Key]));
            }
        }

        #endregion
    }
}