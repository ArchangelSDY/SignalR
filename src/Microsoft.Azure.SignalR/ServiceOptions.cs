// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.SignalR
{
    public class ServiceOptions
    {
        public static readonly TimeSpan DefaultJwtBearerLifetime = TimeSpan.FromSeconds(30);
        public static readonly int DefaultConnectionNumber = 3;
        public static readonly string DefaultApiVersion = "v1-preview";

        public TimeSpan JwtBearerLifetime { get; set; } = DefaultJwtBearerLifetime;

        public int ConnectionNumber { get; set; } = DefaultConnectionNumber;

        public string ApiVersion { get; set; } = DefaultApiVersion;
    }
}
