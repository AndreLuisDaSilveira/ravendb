﻿// -----------------------------------------------------------------------
//  <copyright file="AdminDatabases.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Raven.Imports.Newtonsoft.Json;
//using Raven.Imports.Newtonsoft.Json;
using Raven.Server.Json;
using Raven.Server.ServerWide;
using JsonTextReader = Newtonsoft.Json.JsonTextReader;

namespace Raven.Server.Web.System
{
    public class AdminDatabases : RequestHandler
    {
        private readonly ServerStore _serverStore;

        public AdminDatabases(ServerStore serverStore)
        {
            _serverStore = serverStore;
        }

        public override Task Get(HttpContext ctx)
        {
            RavenOperationContext context;
            using (_serverStore.AllocateRequestContext(out context))
            {
                var dbId = "db/"+ ctx.Request.Query["id"];
                var obj = _serverStore.Read(context, dbId);
                if (obj == null)
                {
                    ctx.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }
                ctx.Response.StatusCode = 200;
                obj.WriteTo(ctx.Response.Body);
                return Task.CompletedTask;
            }
        }

        public override Task Put(HttpContext ctx)
        {
            RavenOperationContext context;
            using (_serverStore.AllocateRequestContext(out context))
            {
                var dbId = "db/" + ctx.Request.Query["id"];

                var writer = context.Read(ctx.Request.Body,  dbId);

                _serverStore.Write(dbId, writer);

                ctx.Response.StatusCode = 201;

                return Task.CompletedTask;
            }
        }
    }
}