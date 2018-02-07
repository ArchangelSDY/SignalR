// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.Azure.SignalR
{
    public interface IServiceClientProxy
    {
        /// <summary>
        /// Invokes a method on the connection(s) in SignalR service represented by the <see cref="IServiceClientProxy"/> instance.
        /// </summary>
        /// <param name="method">name of the method to invoke</param>
        /// <param name="args">arguments to pass to the client</param>
        /// <returns>A task that represents when the data has been sent to the client in SignalR service.</returns>
        Task<HttpResponseMessage> InvokeAsync(string method, object[] args);
    }
}
