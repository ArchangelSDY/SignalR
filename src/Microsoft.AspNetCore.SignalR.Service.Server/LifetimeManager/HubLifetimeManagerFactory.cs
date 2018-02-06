// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.SignalR
{
    public class HubLifetimeManagerFactory : IHubLifetimeManagerFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public HubLifetimeManagerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public ExHubLifetimeManager<THub> Create<THub>(string hubName) where THub : Hub
        {
            return new ServiceHubLifetimeManager<THub>(hubName,
                _loggerFactory.CreateLogger<ServiceHubLifetimeManager<THub>>());
        }
    }
}
