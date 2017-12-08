// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR.Service.Core;
using SocketsSample.Hubs;

namespace SocketsSample
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; }
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);
            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddSockets();

            LogLevel logLevel = LogLevel.Information;
            string consoleLogLevel = Configuration.GetSection("SignalRServerService").GetValue<string>("ConsoleLogLevel");
            switch (consoleLogLevel)
            {
                case "Debug":
                    logLevel = LogLevel.Debug;
                    break;
                case "Information":
                    logLevel = LogLevel.Information;
                    break;
                case "Trace":
                    logLevel = LogLevel.Trace;
                    break;
                default:
                    logLevel = LogLevel.Information;
                    break;
            }
            services.AddSignalRService(hubOption => { hubOption.ConsoleLogLevel = logLevel; });
            services.AddCors(o =>
            {
                o.AddPolicy("Everything", p =>
                {
                    p.AllowAnyHeader()
                     .AllowAnyMethod()
                     .AllowAnyOrigin();
                });
            });
            //services.AddEndPoint<MessagesEndPoint>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseFileServer();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors("Everything");

            app.UseSignalRService(configHub =>
            {
                string signalrServicePath = Configuration.GetSection("SignalRServerService").GetValue<string>("RootPath");
                configHub.BuildServiceHub<Chat>(signalrServicePath + "/server/default");
                configHub.BuildServiceHub<DynamicChat>(signalrServicePath + "/server/dynamic");
                configHub.BuildServiceHub<Streaming>(signalrServicePath + "/server/streaming");
                configHub.BuildServiceHub<HubTChat>(signalrServicePath + "/server/hubT");
            });
        }
    }
}
