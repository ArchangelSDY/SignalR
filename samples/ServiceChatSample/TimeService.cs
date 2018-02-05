using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace ServiceChatSample
{
    public class TimeService
    {
        private readonly SignalR _signalr;
        private readonly HubProxy _hubProxy;
        private readonly Timer _timer;

        public TimeService(SignalR signalr)
        {
            _signalr = signalr;
            _hubProxy = _signalr.CreateHubProxy<Chat>();
            _timer = new Timer(Run, this, 100, 60 * 1000);
        }

        private static void Run(object state)
        {
            _ = ((TimeService) state).Broadcast();
        }

        private async Task Broadcast()
        {
            await _hubProxy.All.InvokeAsync("broadcastMessage",
                new object[]
                {
                    "_BROADCAST_",
                    DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)
                });
        }
    }
}
