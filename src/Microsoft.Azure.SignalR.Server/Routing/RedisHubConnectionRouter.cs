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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.Azure.SignalR
{
    public class RedisHubConnectionRouter : IHubConnectionRouter, IDisposable
    {
        private const int ScanIntervalInSeconds = 5;
        private const long ScanIntervalInTicks = ScanIntervalInSeconds * TimeSpan.TicksPerSecond;
        private const long ExpireIntervalInTicks = 3 * ScanIntervalInTicks;

        private readonly IConnectionMultiplexer _redisConnection;
        private readonly IDatabase _redisDatabase;
        private readonly ILogger _logger;
        private readonly IRoutingCache _cache;
        private readonly string _serviceId;
        private readonly string _instanceId;
        private readonly Timer _timer;

        private readonly object _lock = new object();
        private readonly object _syncLock = new object();

        /* Store "connected" client number of all hub servers, which are physcially connected to this service instance
         *
         * Key: Hub Name
         * Value: 
         *     Key: Server Id
         *     Value: Total number of clients "connected" to this server
         */
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _hubServerStatus =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();

        /* Store "connected" client number of all connections from each hub server.
         * Connections are physically connected to this service instance.
         *
         * Key: {Hub Name}:{Server Id}
         * Value: 
         *     Key: Connection Id (from app server)
         *     Value: Total connected clients to this connection
         */
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _connectionStatus =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();

        // TODO: inject dependency of config provider, so that we can change routing algorithm without restarting service
        public RedisHubConnectionRouter(ILogger<RedisHubConnectionRouter> logger, IRoutingCache cache,
            IOptions<RedisOptions> redisOptions, IOptions<ServerOptions> serverOptions)
        {
            _logger = logger;
            _cache = cache;
            _serviceId = serverOptions.Value?.ServiceId ?? string.Empty;
            _instanceId = serverOptions.Value?.InstanceId ?? Guid.NewGuid().ToString();
            (_redisConnection, _redisDatabase) = ConnectToRedis(logger, redisOptions.Value);
            _timer = new Timer(Scan, this, TimeSpan.FromMilliseconds(0), TimeSpan.FromSeconds(ScanIntervalInSeconds));
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
                _logger.LogWarning("No available hub server found for client connection {0}",
                    connection.TryGetUid(out var uid) ? uid : connection.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Client connection {0} is routed to {1}",
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
                serverStatus = _hubServerStatus.GetOrAdd(hubName, _ => new ConcurrentDictionary<string, int>());
                connectionStatus = _connectionStatus.GetOrAdd($"{hubName}:{serverId}", _ => new ConcurrentDictionary<string, int>());
            }

            serverStatus?.TryAdd(serverId, 0);
            connectionStatus?.TryAdd(connectionId, 0);

            // For new connection, below steps are executed in Redis:
            // 1. Set connection-2-server mapping in service instance's hashset for offline instance cleanup.
            // 2. Add hub server to hub's sorted set for server-level routing
            // 3. Add connection to hub server's sorted set for connection-level routing
            ScriptEvaluate(
                @"redis.call('hset', KEYS[5], KEYS[4], KEYS[2])
                redis.call('zincrby', KEYS[1], 0, KEYS[2])
                redis.call('zincrby', KEYS[3], 0, KEYS[4])",
                new RedisKey[]
                {
                    HubServerStatusSortedSetKey(hubName), serverId,
                    HubServerInstanceSortedSetKey(hubName, serverId), connectionId,
                    ServiceInstanceHashsetKey(hubName)
                });
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
                        if (_hubServerStatus.TryGetValue(hubName, out var serverStatus))
                        {
                            serverStatus.TryRemove(serverId, out _);
                        }
                    }
                }
            }

            // When connection is closed, below steps are executed in Redis:
            // 1. Decrease hub server's score in hub's sorted set
            // 2. Remove connection from hub server's sorted set
            // 3. When hub server's sorted set contains no connection, remove hub server from hub's sorted set
            // 4. Delete connection from instance's hashset
            ScriptEvaluate(
                @"local score = redis.call('zscore', KEYS[1], KEYS[2])
                if score ~= false then
                    redis.call('zincrby', KEYS[3], 0 - tonumber(score), KEYS[4])
                end
                redis.call('zrem', KEYS[1], KEYS[2])
                if redis.call('zcard', KEYS[1]) == 0 then
                    redis.call('zrem', KEYS[3], KEYS[4])
                end
                redis.call('hdel', KEYS[5], KEYS[2])",
                new RedisKey[] 
                {
                    HubServerInstanceSortedSetKey(hubName, serverId), connectionId,
                    HubServerStatusSortedSetKey(hubName), serverId,
                    ServiceInstanceHashsetKey(hubName)
                });
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
            if (_hubServerStatus.TryGetValue(hubName, out var serverStatus))
            {
                serverStatus.TryUpdate(serverId, c => c + value);
            }

            if (_connectionStatus.TryGetValue($"{hubName}:{serverId}", out var connectionStatus))
            {
                connectionStatus.TryUpdate(connectionId, c => c + value);
            }

            // Steps to update "connected" client connection count:
            // 1. If hub server exists in hub's sorted set, update the score.
            // 2. If connection exists in hub server's sorted set, update the score.
            await ScriptEvaluateAsync(
                @"if redis.call('zscore', KEYS[1], KEYS[2]) ~= false then
                    redis.call('zincrby', KEYS[1], ARGV[1], KEYS[2])
                end
                if redis.call('zscore', KEYS[3], KEYS[4]) ~= false then
                    redis.call('zincrby', KEYS[3], ARGV[1], KEYS[4])
                end",
                new RedisKey[]
                {
                    HubServerStatusSortedSetKey(hubName), serverId,
                    HubServerInstanceSortedSetKey(hubName, serverId), connectionId
                },
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
                if (!_hubServerStatus.TryGetValue(hubName, out var serverStatus) || serverStatus.IsEmpty) return null;

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
                // No target server. Try to find hub server with least connection in Redis.
                // Detailed steps:
                // 1. If no hub server available, return empty result; else proceed to next step.
                // 2. Find the hub server "connected" with least client connections.
                // 3. Find the connection "connected" with least client connections.
                // 4. Insert connection Id and hub server to result list and return.
                _logger.LogInformation("Target hub server Id is null. Try to find hub server 'connected' with least client connections in Redis");
                results = (RedisValue[]) ScriptEvaluate(
                    @"local ret = {}
                    if redis.call('zcard', KEYS[1]) ~= 0 then
                        local target_server = redis.call('zrangebyscore', KEYS[1], '-inf', '+inf', 'LIMIT', '0', '1')
                        local server_key = KEYS[2]..':'..target_server[1]
                        local target_conn = redis.call('zrangebyscore', server_key, '-inf', '+inf', 'LIMIT', '0', '1')
                        table.insert(ret, target_conn)
                        table.insert(ret, target_server[1])
                    end
                    return ret",
                    new RedisKey[]
                    {
                        HubServerStatusSortedSetKey(hubName),
                        HubServerInstanceSortedSetPrefix(hubName)
                    });
            }
            else
            {
                // Try to find target hub server.
                // Detailed steps:
                // 1. If target hub server exists and has server connections available,
                //    find connection "connected" with least client connections.
                //    Insert connection id into result list and return.
                // 2. If target hub server doesn't exist, fall back to search for any available hub server.
                //   2.1 Find the hub server "connected" with least client connections.
                //   2.2 Find the connection "connected" with least client connections.
                //   2.3 Insert connection Id and hub server to result list and return.
                _logger.LogInformation($"Target hub server Id is [{serverId}]. Try to find target server in Redis");
                results = (RedisValue[]) ScriptEvaluate(
                    @"local ret = {}
                    local target_conn
                    if redis.call('zcard', KEYS[1]) ~= 0 then
                        target_conn = redis.call('zrangebyscore', KEYS[1], '-inf', '+inf', 'LIMIT', '0', '1')
                        table.insert(ret, target_conn)
                    elseif redis.call('zcard', KEYS[2]) ~= 0 then
                        local target_server = redis.call('zrangebyscore', KEYS[2], '-inf', '+inf', 'LIMIT', '0', '1')
                        local server_key = KEYS[3]..':'..target_server[1]
                        target_conn = redis.call('zrangebyscore', server_key, '-inf', '+inf', 'LIMIT', '0', '1')
                        table.insert(ret, target_conn)
                        table.insert(ret, target_server[1])
                    end
                    return ret",
                    new RedisKey[]
                    {
                        HubServerInstanceSortedSetKey(hubName, serverId),
                        HubServerStatusSortedSetKey(hubName),
                        HubServerInstanceSortedSetPrefix(hubName)
                    });
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

        #region Periodic Scan

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
                _logger.LogDebug("Start syncing data from Redis");

                var tasks = _hubServerStatus.Keys.Select(CleanupOfflineInstances);
                await Task.WhenAll(tasks);

                tasks = _hubServerStatus.Keys.Select(SyncHubServerStatus);
                await Task.WhenAll(tasks);

                _logger.LogDebug("Completed syncing data from Redis");
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

        private async Task CleanupOfflineInstances(string hubName)
        {
            await Task.CompletedTask;

            _logger.LogInformation($"Start cleaning up offline service instances for hub [{hubName}]");

            // Update instance's timestamp
            _redisDatabase.HashSet(ServiceStatusHashsetKey(hubName), new []
            {
                new HashEntry(_instanceId, DateTime.UtcNow.Ticks)
            });

            // Scan for offline instances:
            // 0. If last-scanned timestamp is within scan interval, skip scan and return empty list;
            //    else update the last-scanned timestamp with current time.
            // 1. Get all instances from hub's status hashset
            // 2. Iterate each instance:
            //   2.1 Get instance's last updated timestamp
            //   2.2 If timestamp is after the expire limit, start over the loop with next instance
            //   2.3 If timestamp is before the expire limit:
            //     a. Insert the instance name to inactive_instances list
            //     b. Get all connections from instance's hashset
            //     c. Publish all inactive connections to offline notice channel (as a comma seperated list)
            //     d. Iterate all inactive connections:
            //       d.1 Get hub server of the connection
            //       d.2 Get connection's score from server's sorted set
            //       d.3 Remove connection from server's sorted set
            //       d.4 If server has no connections, remove it from hub's sorted set;
            //           else decrease server's score in hub's sorted set
            //     e. Delete instance's hashset
            //     f. Delete instance from hub's status hashset
            var results = (RedisValue[]) await ScriptEvaluateAsync(
                @"local inactive_instances = {}
                local lastscanned = redis.call('get', KEYS[1])
                if lastscanned ~= false and tonumber(lastscanned) > tonumber(ARGV[1]) then
                    return inactive_instances
                else
                    redis.call('set', KEYS[1], ARGV[2])
                end

                local instance_list = redis.call('hkeys', KEYS[2])
                for _, instance in pairs(instance_list) do
                    local lastactive = redis.call('hget', KEYS[2], instance)
                    if lastactive < ARGV[3] then
                        table.insert(inactive_instances, instance)
                        local inactive_connections = redis.call('hkeys', KEYS[3]..':'..instance)
                        redis.call('publish', KEYS[4], table.concat(inactive_connections, ','))
                        for _, connection in pairs(inactive_connections) do
                            local hub_server = redis.call('hget', KEYS[3]..':'..instance, connection)
                            if hub_server ~= false then
                                local score = redis.call('zscore', KEYS[5]..':'..hub_server, connection)
                                if score ~= false then
                                    redis.call('zrem', KEYS[5]..':'..hub_server, connection)
                                    if redis.call('zcard', KEYS[5]..':'..hub_server) == 0 then
                                        redis.call('zrem', KEYS[6], hub_server)
                                    else
                                        redis.call('zincrby', KEYS[6], 0 - tonumber(score), hub_server)
                                    end
                                end
                            end
                        end
                        redis.call('del', KEYS[3]..':'..instance)
                        redis.call('hdel', KEYS[2], instance)
                    end
                end
                return inactive_instances
                ",
                new RedisKey[]
                {
                    ServiceStatusLastScanKey(hubName),
                    ServiceStatusHashsetKey(hubName),
                    ServiceInstanceHashsetPrefix(hubName),
                    OfflineNoticeChannel(hubName),
                    HubServerInstanceSortedSetPrefix(hubName),
                    HubServerStatusSortedSetKey(hubName)
                },
                new RedisValue[]
                {
                    DateTime.UtcNow.Ticks - ScanIntervalInTicks,
                    DateTime.UtcNow.Ticks,
                    DateTime.UtcNow.Ticks - ExpireIntervalInTicks
                }
            );

            if (results != null && results.Length > 0)
            {
                _logger.LogInformation($"Below service instances are inactive and cleaned up: {string.Join(",", results)}");
            }
            else
            {
                _logger.LogInformation("No inactive servers found.");
            }

            _logger.LogInformation($"Completed cleaning up offline service instances for hub [{hubName}]");
        }

        private async Task SyncHubServerStatus(string hubName)
        {
            if (!_hubServerStatus.TryGetValue(hubName, out var serverStatus) || serverStatus.IsEmpty)
            {
                _logger.LogInformation($"No hub server found for hub [{hubName}]. Skip syncing hub server status.");
                return;
            }

            // Steps to sync hub server status:
            // 1. Get hub server's score and insert into returned list
            // 2. For each hub server, insert connection Id and its score into returned list
            _logger.LogInformation($"Start syncing data for hub [{hubName}]");
            var results = (RedisValue[]) await ScriptEvaluateAsync(
                @"local ret = {}
                for _, server in pairs(ARGV) do
                    table.insert(ret, server)
                    table.insert(ret, redis.call('zscore', KEYS[1], server))
                    for _, v in pairs(redis.call('zrangebyscore', KEYS[2]..':'..server, '-inf', '+inf', 'WITHSCORES')) do
                        table.insert(ret, v)
                    end
                end
                return ret
                ",
                new RedisKey[]
                {
                    HubServerStatusSortedSetKey(hubName),
                    HubServerInstanceSortedSetPrefix(hubName)
                },
                serverStatus.Select(s => (RedisValue) s.Key).ToArray());

            var dict = ConvertResultToDict(results);

            UpdateLocalStatus(hubName, dict);

            _logger.LogInformation($"Completed syncing data for hub [{hubName}]");
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
            if (!_hubServerStatus.TryGetValue(hubName, out var serverStatus)) return;

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

        private RedisResult ScriptEvaluate(string script, RedisKey[] keys = null, RedisValue[] values = null,
            CommandFlags flags = CommandFlags.None)
        {
            var startTicks = DateTime.UtcNow.Ticks;
            var results = _redisDatabase.ScriptEvaluate(script, keys, values, flags);
            var endTicks = DateTime.UtcNow.Ticks;
            _logger.LogInformation($"Redis ScriptEvaluate execution time: {(endTicks - startTicks) / 10000.0} ms");
            return results;
        }

        private async Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[] keys = null, RedisValue[] values = null,
            CommandFlags flags = CommandFlags.None)
        {
            var startTicks = DateTime.UtcNow.Ticks;
            var results = await _redisDatabase.ScriptEvaluateAsync(script, keys, values, flags);
            var endTicks = DateTime.UtcNow.Ticks;
            _logger.LogInformation($"Redis ScriptEvaluateAsync execution time: {(endTicks - startTicks) / 10000.0} ms");
            return results;
        }

        #endregion

        #region Redis Keys

        private string HubPrefix(string hubName)
        {
            return $"{_serviceId}:{hubName}";
        }

        private string ServiceStatusLastScanKey(string hubName)
        {
            return $"{HubPrefix(hubName)}:svc:lastscanned";
        }

        private string ServiceStatusHashsetKey(string hubName)
        {
            return $"{HubPrefix(hubName)}:svc:status";
        }

        private string ServiceInstanceHashsetPrefix(string hubName)
        {
            return $"{HubPrefix(hubName)}:svc:inst";
        }

        private string ServiceInstanceHashsetKey(string hubName)
        {
            return $"{ServiceInstanceHashsetPrefix(hubName)}:{_instanceId}";
        }

        private string OfflineNoticeChannel(string hubName)
        {
            // TODO: remove the hard-coded client prefix here.
            var clientHub = $"client.{hubName}";
            return $"{HubPrefix(clientHub)}:svc:offline";
        }

        private string HubServerStatusSortedSetKey(string hubName)
        {
            return $"{HubPrefix(hubName)}:hubsvr:status";
        }

        private string HubServerInstanceSortedSetPrefix(string hubName)
        {
            return $"{HubPrefix(hubName)}:hubsvr:inst";
        }

        private string HubServerInstanceSortedSetKey(string hubName, string serverId)
        {
            return $"{HubServerInstanceSortedSetPrefix(hubName)}:{serverId}";
        }

        #endregion
    }
}