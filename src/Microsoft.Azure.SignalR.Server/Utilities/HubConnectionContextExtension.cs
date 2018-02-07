// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Azure.SignalR
{
    public static class HubConnectionContextExtension
    {
        public static bool TryGetRouteTarget(this HubConnectionContext connection, out RouteTarget target)
        {
            target = null;
            if (connection.Metadata.TryGetValue("Target", out var value) )
            {
                target = (RouteTarget)value;
            }
            return target != null;
        }

        public static void AddRouteTarget(this HubConnectionContext connection, RouteTarget target)
        {
            connection.Metadata.Add("Target", target);
        }

        public static bool TryGetUid(this HubConnectionContext connection, out string uid)
        {
            if (connection.Metadata.TryGetValue("UID", out var value))
            {
                uid = (string) value;
            }
            else
            {
                uid = null;
            }
            return !string.IsNullOrEmpty(uid);
        }

        public static void AddUid(this HubConnectionContext connection, string uid)
        {
            connection.Metadata.Add("UID", uid);
        }

        public static string GetHubName(this HubConnectionContext connection)
        {
            return connection.GetHttpContext()?.Request.Query["hub"];
        }
    }
}
