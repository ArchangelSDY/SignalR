using System.Configuration;
using System.Security.Claims;
using System.Web.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ServiceChatNetFxSample
{
    [RoutePrefix("signalr-auth")]
    public class SignalRAuthController : Controller
    {
        public static SignalR SignalRInstance = 
            SignalR.Parse(ConfigurationManager.AppSettings["SignalRServiceConnectionString"]);

        // GET signalr-auth/chat
        [Route("{hubName}")]
        [AcceptVerbs("Get")]
        public string Auth(string hubName)
        {
            var userName = HttpContext.Request.QueryString["uid"];
            return string.IsNullOrEmpty(userName)
                ? SignalRInstance.GenerateClientToken(hubName)
                : SignalRInstance.GenerateClientToken(hubName, new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userName)
                });
        }
    }
}
