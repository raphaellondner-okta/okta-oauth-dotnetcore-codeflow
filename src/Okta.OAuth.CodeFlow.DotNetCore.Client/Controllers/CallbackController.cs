﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling MVC for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Okta.OAuth.CodeFlow.DotNetCore.Client.Controllers
{
    public class CallbackController : Controller
    {
        // GET: /<controller>/
        //public IActionResult Index()
        //{
        //    return View();
        //}

        public string Index()
        {
            return "This is the callback page - TBI";
        }
    }
}
