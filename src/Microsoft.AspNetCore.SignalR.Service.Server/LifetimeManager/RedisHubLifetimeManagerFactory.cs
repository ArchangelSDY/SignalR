// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.SignalR.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR
{
    public class RedisHubLifetimeManagerFactory : IHubLifetimeManagerFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IOptionsFactory<RedisOptions> _redisOptionsFactory;
        private readonly ServerOptions _options;

        public RedisHubLifetimeManagerFactory(IOptions<ServerOptions> serverOptions,
            IOptionsFactory<RedisOptions> redisOptionsFactory, ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _redisOptionsFactory = redisOptionsFactory;
            _options = serverOptions.Value;
        }

        public ExHubLifetimeManager<THub> Create<THub>(string hubName) where THub : Hub
        {
            return new RedisHubLifetimeManager<THub>(
                _loggerFactory.CreateLogger<RedisHubLifetimeManager<THub>>(),
                _redisOptionsFactory.Create(string.Empty),
                $"{_options.ServiceId}:{hubName}");
        }
    }
}
