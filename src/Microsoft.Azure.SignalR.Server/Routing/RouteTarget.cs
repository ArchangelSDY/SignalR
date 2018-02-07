// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.SignalR
{
    public class RouteTarget
    {
        public string ConnectionId { get; set; }

        public string ServerId { get; set; }

        public override string ToString()
        {
            return $"{ConnectionId}:{ServerId}";
        }

        public static RouteTarget FromString(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;

            var targets = target.Split(new[] {":"}, 2, StringSplitOptions.RemoveEmptyEntries);
            return new RouteTarget
            {
                ConnectionId = targets[0],
                ServerId = targets.Length > 1 ? targets[1] : targets[0]
            };
        }
    }
}
