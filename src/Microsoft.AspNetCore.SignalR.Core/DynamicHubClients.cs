﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNetCore.SignalR
{
    public class DynamicHubClients
    {
        private readonly IHubClients _clients;

        public DynamicHubClients(IHubClients clients)
        {
            _clients = clients;
        }

        public dynamic All => new DynamicClientProxy(_clients.All);
        public dynamic AllExcept(params string[] excludedIds) => new DynamicClientProxy(_clients.AllExcept(excludedIds));
        public dynamic User(string userId) => new DynamicClientProxy(_clients.User(userId));
        public dynamic Group(string group) => new DynamicClientProxy(_clients.Group(group));
        public dynamic Client(string connectionId) => new DynamicClientProxy(_clients.Client(connectionId));
    }
}