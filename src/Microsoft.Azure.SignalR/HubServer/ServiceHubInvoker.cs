// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Azure.SignalR.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.SignalR
{
    public class ServiceHubInvoker<THub> : IHubInvoker<THub> where THub : Hub
    {
        private readonly Dictionary<string, HubMethodDescriptor> _methods =
            new Dictionary<string, HubMethodDescriptor>(StringComparer.OrdinalIgnoreCase);

        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<THub> _hubContext;
        private readonly ILogger<ServiceHubInvoker<THub>> _logger;

        public ServiceHubInvoker(IServiceProvider serviceProvider, IHubContext<THub> hubContext, ILogger<ServiceHubInvoker<THub>> logger)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _logger = logger;

            DiscoverHubMethods();
        }

        #region Implementation of IHubInvoker

        public async Task OnConnectedAsync(HubConnectionContext connection)
        {
            try
            {
                var hubActivator = _serviceProvider.GetRequiredService<IHubActivator<THub>>();
                var hub = hubActivator.Create();
                try
                {
                    InitializeHub(hub, connection);
                    await hub.OnConnectedAsync();
                }
                finally
                {
                    hubActivator.Release(hub);
                }
            }
            catch (Exception)
            {
                //_logger.ErrorInvokingHubMethod("OnConnectedAsync", ex);
                throw;
            }
        }

        public async Task OnDisconnectedAsync(HubConnectionContext connection, Exception exception)
        {
            try
            {
                // We wait on abort to complete, this is so that we can guarantee that all callbacks have fired
                // before OnDisconnectedAsync

                //try
                //{
                //    // Ensure the connection is aborted before firing disconnect
                //    await connection.AbortAsync();
                //}
                //catch (Exception ex)
                //{
                //    //_logger.AbortFailed(ex);
                //}

                var hubActivator = _serviceProvider.GetRequiredService<IHubActivator<THub>>();
                var hub = hubActivator.Create();
                try
                {
                    InitializeHub(hub, connection);
                    await hub.OnConnectedAsync();
                }
                finally
                {
                    hubActivator.Release(hub);
                }
            }
            catch (Exception)
            {
                //_logger.ErrorInvokingHubMethod("OnDisconnectedAsync", ex);
                throw;
            }
        }

        public async Task OnInvocationAsync(HubConnectionContext connection, HubMethodInvocationMessage message,
            bool isStreamedInvocation)
        {
            try
            {
                // If an unexpected exception occurs then we want to kill the entire connection
                // by ending the processing loop
                if (!_methods.TryGetValue(message.Target, out var descriptor))
                {
                    // Send an error to the client. Then let the normal completion process occur
                    //_logger.UnknownHubMethod(message.Target);
                    await SendMessageAsync(connection, CompletionMessage.WithError(
                        message.InvocationId, $"Unknown hub method '{message.Target}'"));
                }
                else
                {
                    await Invoke(descriptor, connection, message, isStreamedInvocation);
                }
            }
            catch (Exception)
            {
                //// Abort the entire connection if the invocation fails in an unexpected way
                //connection.Abort(ex);
            }
        }

        public async Task OnCompletionAsync(HubConnectionContext connection, CompletionMessage message)
        {
            await Task.CompletedTask;
        }

        #endregion

        #region Implementation of IInvocationBinder

        Type IInvocationBinder.GetReturnType(string invocationId)
        {
            return typeof(object);
        }

        Type[] IInvocationBinder.GetParameterTypes(string methodName)
        {
            return !_methods.TryGetValue(methodName, out var descriptor) ? Type.EmptyTypes : descriptor.ParameterTypes;
        }

        #endregion

        #region Private Methods

        private async Task SendMessageAsync(HubConnectionContext connection, HubMessage hubMessage)
        {
            while (await connection.Output.Writer.WaitToWriteAsync())
            {
                if (connection.Output.Writer.TryWrite(hubMessage))
                {
                    return;
                }
            }

            // Output is closed. Cancel this invocation completely
            //_logger.OutboundChannelClosed();
            throw new OperationCanceledException("Outbound channel was closed while trying to write hub message");
        }

        private async Task Invoke(HubMethodDescriptor descriptor, HubConnectionContext connection,
            HubMethodInvocationMessage hubMethodInvocationMessage, bool isStreamedInvocation)
        {
            var methodExecutor = descriptor.MethodExecutor;

            if (!await IsHubMethodAuthorized(_serviceProvider, connection.User, descriptor.Policies))
            {
                //_logger.HubMethodNotAuthorized(hubMethodInvocationMessage.Target);
                await SendInvocationError(hubMethodInvocationMessage, connection,
                    $"Failed to invoke '{hubMethodInvocationMessage.Target}' because user is unauthorized");
                return;
            }

            if (!await ValidateInvocationMode(methodExecutor.MethodReturnType, isStreamedInvocation,
                hubMethodInvocationMessage, connection))
            {
                return;
            }

            var hubActivator = _serviceProvider.GetRequiredService<IHubActivator<THub>>();
            var hub = hubActivator.Create();

            try
            {
                InitializeHub(hub, connection);

                var result = await ExecuteHubMethod(methodExecutor, hub, hubMethodInvocationMessage.Arguments);

                if (isStreamedInvocation)
                {
                    var enumerator = GetStreamingEnumerator(connection, hubMethodInvocationMessage.InvocationId,
                        methodExecutor, result, methodExecutor.MethodReturnType);
                    //_logger.StreamingResult(hubMethodInvocationMessage.InvocationId, methodExecutor.MethodReturnType.FullName);
                    await StreamResultsAsync(hubMethodInvocationMessage.InvocationId, connection, enumerator);
                }
                // Non-empty/null InvocationId ==> Blocking invocation that needs a response
                else if (!string.IsNullOrEmpty(hubMethodInvocationMessage.InvocationId))
                {
                    //_logger.SendingResult(hubMethodInvocationMessage.InvocationId, methodExecutor.MethodReturnType.FullName);
                    await SendMessageAsync(connection,
                        CompletionMessage.WithResult(hubMethodInvocationMessage.InvocationId, result));
                }
            }
            catch (TargetInvocationException ex)
            {
                //_logger.FailedInvokingHubMethod(hubMethodInvocationMessage.Target, ex);
                await SendInvocationError(hubMethodInvocationMessage, connection, ex.InnerException.Message);
            }
            catch (Exception ex)
            {
                //_logger.FailedInvokingHubMethod(hubMethodInvocationMessage.Target, ex);
                await SendInvocationError(hubMethodInvocationMessage, connection, ex.Message);
            }
            finally
            {
                hub?.Dispose();
            }
        }

        private static async Task<object> ExecuteHubMethod(ObjectMethodExecutor methodExecutor, THub hub, object[] arguments)
        {
            // ReadableChannel is awaitable but we don't want to await it.
            if (methodExecutor.IsMethodAsync && !IsChannel(methodExecutor.MethodReturnType, out _))
            {
                if (methodExecutor.MethodReturnType == typeof(Task))
                {
                    await (Task)methodExecutor.Execute(hub, arguments);
                }
                else
                {
                    return await methodExecutor.ExecuteAsync(hub, arguments);
                }
            }
            else
            {
                return methodExecutor.Execute(hub, arguments);
            }

            return null;
        }

        private async Task SendInvocationError(HubMethodInvocationMessage hubMethodInvocationMessage,
            HubConnectionContext connection, string errorMessage)
        {
            if (string.IsNullOrEmpty(hubMethodInvocationMessage.InvocationId))
            {
                return;
            }

            await SendMessageAsync(connection, CompletionMessage.WithError(hubMethodInvocationMessage.InvocationId, errorMessage));
        }

        private void InitializeHub(THub hub, HubConnectionContext connection)
        {
            hub.Clients = new HubCallerClients(_hubContext.Clients, connection.ConnectionId);
            hub.Context = new HubCallerContext(connection);
            hub.Groups = _hubContext.Groups;
        }

        private static bool IsChannel(Type type, out Type payloadType)
        {
            var channelType = type.AllBaseTypes().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ChannelReader<>));
            if (channelType == null)
            {
                payloadType = null;
                return false;
            }
            else
            {
                payloadType = channelType.GetGenericArguments()[0];
                return true;
            }
        }

        private async Task StreamResultsAsync(string invocationId, HubConnectionContext connection, IAsyncEnumerator<object> enumerator)
        {
            string error = null;

            try
            {
                while (await enumerator.MoveNextAsync())
                {
                    // Send the stream item
                    await SendMessageAsync(connection, new StreamItemMessage(invocationId, enumerator.Current));
                }
            }
            catch (Exception ex)
            {
                // If the streaming method was canceled we don't want to send a HubException message - this is not an error case
                if (!(ex is OperationCanceledException && connection.ActiveRequestCancellationSources.TryGetValue(invocationId, out var cts)
                    && cts.IsCancellationRequested))
                {
                    error = ex.Message;
                }
            }
            finally
            {
                await SendMessageAsync(connection, new CompletionMessage(invocationId, error: error, result: null, hasResult: false));

                if (connection.ActiveRequestCancellationSources.TryRemove(invocationId, out var cts))
                {
                    cts.Dispose();
                }
            }
        }

        private async Task<bool> ValidateInvocationMode(Type resultType, bool isStreamedInvocation,
            HubMethodInvocationMessage hubMethodInvocationMessage, HubConnectionContext connection)
        {
            var isStreamedResult = IsStreamed(resultType);
            if (isStreamedResult && !isStreamedInvocation)
            {
                // Non-null/empty InvocationId? Blocking
                if (!string.IsNullOrEmpty(hubMethodInvocationMessage.InvocationId))
                {
                    //_logger.StreamingMethodCalledWithInvoke(hubMethodInvocationMessage);
                    await SendMessageAsync(connection, CompletionMessage.WithError(hubMethodInvocationMessage.InvocationId,
                        $"The client attempted to invoke the streaming '{hubMethodInvocationMessage.Target}' method in a non-streaming fashion."));
                }

                return false;
            }

            if (!isStreamedResult && isStreamedInvocation)
            {
                //_logger.NonStreamingMethodCalledWithStream(hubMethodInvocationMessage);
                await SendMessageAsync(connection, CompletionMessage.WithError(hubMethodInvocationMessage.InvocationId,
                    $"The client attempted to invoke the non-streaming '{hubMethodInvocationMessage.Target}' method in a streaming fashion."));

                return false;
            }

            return true;
        }

        private static bool IsStreamed(Type resultType)
        {
            var observableInterface = IsIObservable(resultType) ?
                resultType :
                resultType.GetInterfaces().FirstOrDefault(IsIObservable);

            if (observableInterface != null)
            {
                return true;
            }

            if (IsChannel(resultType, out _))
            {
                return true;
            }

            return false;
        }

        private IAsyncEnumerator<object> GetStreamingEnumerator(HubConnectionContext connection, string invocationId, ObjectMethodExecutor methodExecutor, object result, Type resultType)
        {
            if (result != null)
            {
                var observableInterface = IsIObservable(resultType) ?
                    resultType :
                    resultType.GetInterfaces().FirstOrDefault(IsIObservable);
                if (observableInterface != null)
                {
                    return AsyncEnumeratorAdapters.FromObservable(result, observableInterface, CreateCancellation());
                }

                if (IsChannel(resultType, out var payloadType))
                {
                    return AsyncEnumeratorAdapters.FromChannel(result, payloadType, CreateCancellation());
                }
            }

            //_logger.InvalidReturnValueFromStreamingMethod(methodExecutor.MethodInfo.Name);
            throw new InvalidOperationException($"The value returned by the streaming method '{methodExecutor.MethodInfo.Name}' is null, does not implement the IObservable<> interface or is not a ReadableChannel<>.");

            CancellationToken CreateCancellation()
            {
                var streamCts = new CancellationTokenSource();
                connection.ActiveRequestCancellationSources.TryAdd(invocationId, streamCts);
                return CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionAbortedToken, streamCts.Token).Token;
            }
        }

        private static bool IsIObservable(Type iface)
        {
            return iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IObservable<>);
        }

        private void DiscoverHubMethods()
        {
            var hubType = typeof(THub);
            var hubTypeInfo = hubType.GetTypeInfo();

            foreach (var methodInfo in HubReflectionHelper.GetHubMethods(hubType))
            {
                var methodName =
                    methodInfo.GetCustomAttribute<HubMethodNameAttribute>()?.Name ??
                    methodInfo.Name;

                if (_methods.ContainsKey(methodName))
                {
                    throw new NotSupportedException($"Duplicate definitions of '{methodName}'. Overloading is not supported.");
                }

                var executor = ObjectMethodExecutor.Create(methodInfo, hubTypeInfo);
                var authorizeAttributes = methodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true);
                _methods[methodName] = new HubMethodDescriptor(executor, authorizeAttributes);

                //_logger.HubMethodBound(methodName);
            }
        }

        private async Task<bool> IsHubMethodAuthorized(IServiceProvider provider, ClaimsPrincipal principal, IList<IAuthorizeData> policies)
        {
            // If there are no policies we don't need to run auth
            if (!policies.Any())
            {
                return true;
            }

            var authService = provider.GetRequiredService<IAuthorizationService>();
            var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

            var authorizePolicy = await AuthorizationPolicy.CombineAsync(policyProvider, policies);
            // AuthorizationPolicy.CombineAsync only returns null if there are no policies and we check that above
            Debug.Assert(authorizePolicy != null);

            var authorizationResult = await authService.AuthorizeAsync(principal, authorizePolicy);
            // Only check authorization success, challenge or forbid wouldn't make sense from a hub method invocation
            return authorizationResult.Succeeded;
        }

        private class HubMethodDescriptor
        {
            public HubMethodDescriptor(ObjectMethodExecutor methodExecutor, IEnumerable<IAuthorizeData> policies)
            {
                MethodExecutor = methodExecutor;
                ParameterTypes = methodExecutor.MethodParameters.Select(p => p.ParameterType).ToArray();
                Policies = policies.ToArray();
            }

            public ObjectMethodExecutor MethodExecutor { get; }

            public Type[] ParameterTypes { get; }

            public IList<IAuthorizeData> Policies { get; }
        }

        #endregion
    }
}
