// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Microsoft.Azure.SignalR
{
    public static class MessageReceivedContextExtensions
    {
        public static bool IsTokenFromQueryString(this MessageReceivedContext context)
        {
            var headers = context.Request.Headers;
            return (headers["Connection"] == "Upgrade" && headers["Upgrade"] == "websocket") ||
                   headers["Accept"] == "text/event-stream";
        }
    }
}
