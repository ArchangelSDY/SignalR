using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.SignalR;

namespace ServiceSample.Controllers
{
    [Route("v1-preview/hub")]
    [Consumes("application/json")]
    public class HubProxyController : Controller
    {
        private const char NameSeparator = ',';

        // TODO: extract interface
        private readonly IHubMessageBroker _messageBroker;

        public HubProxyController(IHubMessageBroker messageBroker)
        {
            _messageBroker = messageBroker;
        }

        // POST v1-preview/hub/chat/connection/1
        [HttpPost("{hubName}/connection/{id}")]
        public async Task<IActionResult> InvokeConnection(string hubName, string id, [FromBody] MethodInvocation message)
        {
            var lifetimeManager = _messageBroker.GetHubLifetimeManager(hubName);
            if (lifetimeManager == null)
            {
                return NotFound($"Hub not exist: {hubName}");
            }

            await lifetimeManager.InvokeConnectionAsync(id, message.Method, message.Arguments);

            return Accepted();
        }

        // POST v1-preview/hub/chat/connections/1,2,3
        [HttpPost("{hubName}/connections/{idList}")]
        public async Task<IActionResult> InvokeConnections(string hubName, string idList,
            [FromBody] MethodInvocation message)
        {
            var lifetimeManager = _messageBroker.GetHubLifetimeManager(hubName);
            if (lifetimeManager == null)
            {
                return NotFound($"Hub not exist: {hubName}");
            }

            var ids = idList.Split(NameSeparator, StringSplitOptions.RemoveEmptyEntries);
            await lifetimeManager.InvokeConnectionsAsync(ids, message.Method, message.Arguments);

            return Accepted();
        }

        // POST v1-preview/hub/chat/user/1
        [HttpPost("{hubName}/user/{id}")]
        public async Task<IActionResult> InvokeUser(string hubName, string id, [FromBody] MethodInvocation message)
        {
            var lifetimeManager = _messageBroker.GetHubLifetimeManager(hubName);
            if (lifetimeManager == null)
            {
                return NotFound($"Hub not exist: {hubName}");
            }

            await lifetimeManager.InvokeUserAsync(id, message.Method, message.Arguments);

            return Accepted();
        }

        // POST v1-preview/hub/chat/users/1,2,3
        [HttpPost("{hubName}/users/{idList}")]
        public async Task<IActionResult> InvokeUsers(string hubName, string idList,
            [FromBody] MethodInvocation message)
        {
            var lifetimeManager = _messageBroker.GetHubLifetimeManager(hubName);
            if (lifetimeManager == null)
            {
                return NotFound($"Hub not exist: {hubName}");
            }

            var ids = idList.Split(NameSeparator, StringSplitOptions.RemoveEmptyEntries);
            await lifetimeManager.InvokeUsersAsync(ids, message.Method, message.Arguments);

            return Accepted();
        }

        // POST v1-preview/hub/chat
        [HttpPost("{hubName}")]
        public async Task<IActionResult> Broadcast(string hubName, [FromBody] MethodInvocationExcept message)
        {
            var lifetimeManager = _messageBroker.GetHubLifetimeManager(hubName);
            if (lifetimeManager == null)
            {
                return NotFound($"Hub not exist: {hubName}");
            }

            if (message.ExcludedIds != null && message.ExcludedIds.Any())
            {
                await lifetimeManager.InvokeAllExceptAsync(message.Method, message.Arguments, message.ExcludedIds);
            }
            else
            {
                await lifetimeManager.InvokeAllAsync(message.Method, message.Arguments);
            }

            return Accepted();
        }

        // POST v1-preview/hub/chat/group/1
        [HttpPost("{hubName}/group/{groupName}")]
        public async Task<IActionResult> GroupBroadcast(string hubName, string groupName,
            [FromBody] MethodInvocationExcept message)
        {
            var lifetimeManager = _messageBroker.GetHubLifetimeManager(hubName);
            if (lifetimeManager == null)
            {
                return NotFound($"Hub not exist: {hubName}");
            }

            if (message.ExcludedIds != null && message.ExcludedIds.Any())
            {
                await lifetimeManager.InvokeGroupExceptAsync(groupName, message.Method, message.Arguments, message.ExcludedIds);
            }
            else
            {
                await lifetimeManager.InvokeGroupAsync(groupName, message.Method, message.Arguments);
            }

            return Accepted();
        }

        // POST v1-preview/hub/chat/groups/1,2,3
        [HttpPost("{hubName}/groups/{groupList}")]
        public async Task<IActionResult> MultiGroupBroadcast(string hubName, string groupList,
            [FromBody] MethodInvocation message)
        {
            var lifetimeManager = _messageBroker.GetHubLifetimeManager(hubName);
            if (lifetimeManager == null)
            {
                return NotFound($"Hub not exist: {hubName}");
            }

            var groups = groupList.Split(NameSeparator, StringSplitOptions.RemoveEmptyEntries);
            await lifetimeManager.InvokeGroupsAsync(groups, message.Method, message.Arguments);

            return Accepted();
        }

        public class MethodInvocation
        {
            public string Method { get; set; }

            public object[] Arguments { get; set; }
        }

        public class MethodInvocationExcept : MethodInvocation
        {
            public string[] ExcludedIds { get; set; }
        }
    }
}
