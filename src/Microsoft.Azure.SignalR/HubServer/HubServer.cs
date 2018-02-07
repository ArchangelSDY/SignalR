// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.Sockets;
using Microsoft.AspNetCore.Sockets.Client;
using Microsoft.AspNetCore.Sockets.Client.Http;

namespace Microsoft.Azure.SignalR
{
    public class HubServer<THub> where THub : Hub
    {
        private readonly List<ServiceConnection<THub>> _serviceConnections = new List<ServiceConnection<THub>>();

        private readonly HubLifetimeManager<THub> _lifetimeManager;
        private readonly IHubInvoker<THub> _hubInvoker;

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<HubServer<THub>> _logger;

        public HubServer(HubLifetimeManager<THub> lifetimeManager,
            IHubInvoker<THub> hubInvoker,
            ILoggerFactory loggerFactory)
        {
            _lifetimeManager = lifetimeManager;
            _hubInvoker = hubInvoker;

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<HubServer<THub>>();
        }

        internal void UseService(SignalRService signalr)
        {
            var serviceUrl = GetServiceUrl(signalr);
            var httpOptions = new HttpOptions
            {
                JwtBearerTokenFactory = signalr.GenerateServerToken<THub>
            };

            for (var i = 0; i < signalr.ConnectionNumber; i++)
            {
                var serviceConnection = CreateServiceConnection(serviceUrl, httpOptions);
                _serviceConnections.Add(serviceConnection);
            }
        }

        public async Task StartAsync()
        {
            try
            {
                var tasks = _serviceConnections.Select(c => c.StartAsync());
                await Task.WhenAll(tasks);
            }
            catch (Exception)
            {
            }
        }

        public async Task StopAsync()
        {
            try
            {
                var tasks = _serviceConnections.Select(c => c.StopAsync());
                await Task.WhenAll(tasks);
            }
            catch (Exception)
            {
            }
        }

        #region Private Methods

        private Uri GetServiceUrl(SignalRService signalr)
        {
            return new Uri(signalr.GetServerUrl<THub>());
        }

        private ServiceConnection<THub> CreateServiceConnection(Uri serviceUrl, HttpOptions httpOptions)
        {
            var httpConnection =
                new HttpConnection(serviceUrl, TransportType.WebSockets, _loggerFactory, httpOptions);
            return new ServiceConnection<THub>(httpConnection, new JsonHubProtocol(), _lifetimeManager, _hubInvoker, _loggerFactory);
        }

        #endregion
    }
}
