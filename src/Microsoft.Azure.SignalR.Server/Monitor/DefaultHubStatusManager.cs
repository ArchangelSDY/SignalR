// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.SignalR
{
    public class DefaultHubStatusManager : IHubStatusManager
    {
        private readonly ConcurrentDictionary<string, int> _clientConnections = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, int> _serverConnections = new ConcurrentDictionary<string, int>();

        private readonly ConcurrentDictionary<string, int> _clientMessages = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, int> _serverMessages = new ConcurrentDictionary<string, int>();

        private long _successCount = 0;
        private long _failureCount = 0;

        public async Task AddClientConnection(string hubName)
        {
            await Task.CompletedTask;
            _clientConnections.AddOrUpdate(hubName, _ => 1, (_, x) => x + 1);
        }

        public async Task AddServerConnection(string hubName)
        {
            await Task.CompletedTask;
            _serverConnections.AddOrUpdate(hubName, _ => 1, (_, x) => x + 1);
        }

        public async Task RemoveClientConnection(string hubName)
        {
            await Task.CompletedTask;
            _clientConnections.TryUpdate(hubName, x => x - 1);
        }

        public async Task RemoveServerConnection(string hubName)
        {
            await Task.CompletedTask;
            _serverConnections.TryUpdate(hubName, x => x - 1);
        }

        public async Task AddClientMessage(string hubName)
        {
            await Task.CompletedTask;
            _clientMessages.AddOrUpdate(hubName, _ => 1, (_, x) => x + 1);
        }

        public async Task AddServerMessage(string hubName)
        {
            await Task.CompletedTask;
            _serverMessages.AddOrUpdate(hubName, _ => 1, (_, x) => x + 1);
        }

        public async Task AddOperation(bool isError = false)
        {
            await Task.CompletedTask;
            if (isError)
            {
                Interlocked.Increment(ref _failureCount);
            }
            else
            {
                Interlocked.Increment(ref _successCount);
            }
        }

        public string GetHubStatus()
        {
            var clientStats = _clientConnections.Select(x => BuildHubStatus(x, _clientMessages)).ToArray();
            var serverStats = _serverConnections.Select(x => BuildHubStatus(x, _serverMessages)).ToArray();
            var clientOverall = new HubStatus
            {
                ConnectionCount = clientStats.Sum(x => x.ConnectionCount),
                MessageCount = clientStats.Sum(x => x.MessageCount)
            };
            var serverOverall = new HubStatus
            {
                ConnectionCount = serverStats.Sum(x => x.ConnectionCount),
                MessageCount = serverStats.Sum(x => x.MessageCount)
            };

            return JsonConvert.SerializeObject(new
            {
                overall = new
                {
                    client = clientOverall,
                    server = serverOverall,
                    success = _successCount,
                    error = _failureCount
                },
                client = clientStats,
                server = serverStats
            });
        }

        private static HubStatus BuildHubStatus(KeyValuePair<string, int> kvp,
            IReadOnlyDictionary<string, int> messageStat)
        {
            return new HubStatus
            {
                HubName = kvp.Key,
                ConnectionCount = kvp.Value,
                MessageCount = messageStat.TryGetValue(kvp.Key, out var count) ? count : 0
            };
        }
    }

    internal class HubStatus
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string HubName { get; set; }

        public int ConnectionCount { get; set; }

        public int MessageCount { get; set; }
    }
}
