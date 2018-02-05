// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.SignalR
{
    public class DistributedRoutingCache : IRoutingCache
    {
        // TODO: enable user to configure retension period
        private static readonly DistributedCacheEntryOptions ExpireIn30Min = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = new TimeSpan(0, 30, 0)
        };

        private readonly IDistributedCache _cache;

        private readonly ServerOptions _options;

        public DistributedRoutingCache(IDistributedCache cache, IOptions<ServerOptions> options)
        {
            _cache = cache;
            _options = options.Value;
        }

        public bool TryGetTarget(HubConnectionContext connection, out RouteTarget target)
        {
            var targetValue = _options.EnableStickySession && connection.TryGetUid(out var uid)
                ? _cache.GetString($"{_options.ServiceId}:{uid}")
                : null;
            target = RouteTarget.FromString(targetValue);
            return target != null;
        }

        public async Task SetTargetAsync(HubConnectionContext connection, RouteTarget target)
        {
            if (_options.EnableStickySession && connection.TryGetUid(out var uid))
            {
                await _cache.SetStringAsync($"{_options.ServiceId}:{uid}", target.ToString());
            }
        }

        public async Task RemoveTargetAsync(HubConnectionContext connection)
        {
            if (_options.EnableStickySession && connection.TryGetUid(out var uid))
            {
                await _cache.RemoveAsync($"{_options.ServiceId}:{uid}");
            }
        }

        public async Task DelayRemoveTargetAsync(HubConnectionContext connection, RouteTarget target)
        {
            if (_options.EnableStickySession && connection.TryGetUid(out var uid))
            {
                await _cache.SetStringAsync($"{_options.ServiceId}:{uid}", target.ToString(), ExpireIn30Min);
            }
        }
    }
}
