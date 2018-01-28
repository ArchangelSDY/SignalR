﻿using Microsoft.AspNetCore.SignalR;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace ServiceChatNetFxSample
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        public static ServiceClient<Chat> ChatClient;

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            //var signalr = SignalR.Parse(ConfigurationManager.AppSettings["SignalRService:ConnectionString"]);
            //ChatClient = signalr.CreateServiceClient<Chat>();
            //_ = ChatClient.StartAsync();
        }
    }
}
