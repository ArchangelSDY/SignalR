using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Azure.SignalR
{
    public class HubProxy : IHubClients<IServiceClientProxy>
    {
        private readonly SignalRService _signalr;

        public string HubName { get; }

        public HubProxy(SignalRService signalr, string hubName)
        {
            _signalr = signalr;
            HubName = hubName;
            All = _signalr.CreateAllClientsProxy(HubName);
        }

        public IServiceClientProxy All { get; }

        public IServiceClientProxy AllExcept(IReadOnlyList<string> excludedIds)
        {
            return _signalr.CreateAllClientsExceptProxy(HubName, excludedIds);
        }

        public IServiceClientProxy Client(string connectionId)
        {
            return _signalr.CreateSingleClientProxy(HubName, connectionId);
        }

        public IServiceClientProxy Clients(IReadOnlyList<string> connectionIds)
        {
            return _signalr.CreateMultipleClientProxy(HubName, connectionIds);
        }

        public IServiceClientProxy Group(string groupName)
        {
            return _signalr.CreateSingleGroupProxy(HubName, groupName);
        }

        public IServiceClientProxy Groups(IReadOnlyList<string> groupNames)
        {
            return _signalr.CreateMultipleGroupProxy(HubName, groupNames);
        }

        public IServiceClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludeIds)
        {
            return _signalr.CreateSingleGroupExceptProxy(HubName, groupName, excludeIds);
        }

        public IServiceClientProxy User(string userId)
        {
            return _signalr.CreateSingleUserProxy(HubName, userId);
        }

        public IServiceClientProxy Users(IReadOnlyList<string> userIds)
        {
            return _signalr.CreateMultipleUserProxy(HubName, userIds);
        }
    }
}
