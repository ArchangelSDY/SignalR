using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.SignalR;

namespace ServiceSample.Controllers
{
    [Route("health")]
    public class HealthController : Controller
    {
        private readonly IHubStatusManager _hubStatusManager;

        public HealthController(IHubStatusManager hubStatusManager)
        {
            _hubStatusManager = hubStatusManager;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return new JsonResult(_hubStatusManager.GetHubStatus());
        }
    }
}
