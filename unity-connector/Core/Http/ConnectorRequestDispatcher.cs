using System;
using System.Collections.Generic;
using UnityCliConnector.Network;

namespace UnityCliConnector.Http
{
    /// <summary>Shared HTTP routes for Editor and Runtime hosts.</summary>
    public sealed class ConnectorRequestDispatcher : IRequestDispatcher
    {
        private readonly ICommandHost _host;
        private readonly ICommandScheduler _scheduler;
        private readonly ICommandQuery _commands;
        private readonly IHealthMetadataProvider _health;

        public ConnectorRequestDispatcher(
            ICommandHost host,
            ICommandScheduler scheduler,
            ICommandQuery commands = null,
            IHealthMetadataProvider health = null)
        {
            _host = host;
            _scheduler = scheduler;
            _commands = commands;
            _health = health;
        }

        public bool TryDispatch(
            string method,
            string path,
            string body,
            Action<int, Dictionary<string, object>> writeJson,
            string authHeader = null)
        {
            if (!ConnectorNetwork.ValidateAuthToken(authHeader))
            {
                writeJson(401, new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = "unauthorized",
                    ["error_code"] = "AUTH_REQUIRED",
                });
                return true;
            }

            if (path == "/health" && method == "GET")
            {
                var payload = new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["host"] = _host.HostName,
                    ["connector_build"] = ConnectorBuild.Id,
                    ["catalog_version"] = CommandCatalog.GetCachedCatalogVersion(_host.HostName),
                    ["bind_mode"] = ConnectorNetwork.ResolveBindMode().ToString().ToLowerInvariant(),
                };
                _health?.AppendHealth(payload);
                writeJson(200, payload);
                return true;
            }

            if (path == "/list" && method == "POST")
            {
                if (_scheduler is IMainThreadHttpScheduler mainThread)
                {
                    mainThread.ScheduleCatalog(writeJson);
                    return true;
                }

                writeJson(200, CommandCatalog.BuildResponse(_host.HostName));
                return true;
            }

            if (path.StartsWith("/commands/", StringComparison.Ordinal) && method == "GET")
            {
                if (_commands == null)
                {
                    writeJson(404, new Dictionary<string, object>
                    {
                        ["ok"] = false,
                        ["error"] = "commands_not_supported",
                    });
                    return true;
                }

                var id = path.Substring("/commands/".Length).Trim('/');
                if (_scheduler is IMainThreadHttpScheduler mainThread)
                {
                    mainThread.ScheduleCommandStatus(id, writeJson);
                    return true;
                }

                var commandPayload = _commands.GetCommandResponse(id);
                if (commandPayload == null)
                {
                    writeJson(404, new Dictionary<string, object> { ["ok"] = false, ["error"] = "command_not_found" });
                    return true;
                }

                writeJson(200, commandPayload);
                return true;
            }

            if (path == "/command" && method == "POST")
            {
                _scheduler.Schedule(body, writeJson);
                return true;
            }

            return false;
        }
    }
}
