﻿using System.Web.Mvc;

namespace ServiceChatNetFxSample
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Title = "Home Page";

            return View();
        }
    }
}