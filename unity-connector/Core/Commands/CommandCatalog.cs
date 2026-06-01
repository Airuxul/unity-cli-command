using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace UnityCliConnector
{
    public static class CommandCatalog
    {
        private static readonly Dictionary<string, string> CachedVersions =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheGate = new();

        public static string GetCatalogVersion(string hostKind) =>
            ComputeVersion(CollectEntries(hostKind).versionParts);

        /// <summary>Last catalog hash built on the main thread (never triggers discovery).</summary>
        public static string GetCachedCatalogVersion(string hostKind)
        {
            lock (CacheGate)
            {
                return CachedVersions.TryGetValue(hostKind, out var version) ? version : null;
            }
        }

        public static void ClearCachedVersions()
        {
            lock (CacheGate)
            {
                CachedVersions.Clear();
            }
        }

        public static Dictionary<string, object> BuildResponse(string hostKind)
        {
            var collected = CollectEntries(hostKind);
            var version = ComputeVersion(collected.versionParts);
            lock (CacheGate)
            {
                CachedVersions[hostKind] = version;
            }

            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["catalog_version"] = version,
                ["commands"] = collected.commands,
                ["alias_to_command"] = collected.aliasToCommand,
            };
        }

        private static (List<Dictionary<string, object>> commands, List<string> versionParts, Dictionary<string, string> aliasToCommand)
            CollectEntries(string hostKind)
        {
            var commands = new List<Dictionary<string, object>>();
            var versionParts = new List<string>();
            var aliasToCommand = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var handler in CommandDiscovery.Handlers)
            {
                if (!CommandAvailability.IsAvailableForHost(handler.Scope, hostKind))
                    continue;

                var completion = CommandCompletionCatalog.GetCompletionKind(handler.Name);
                var aliases = handler.Aliases ?? Array.Empty<string>();
                var paramDescs = handler.ParamDescriptions ?? Array.Empty<string>();

                commands.Add(new Dictionary<string, object>
                {
                    ["name"] = handler.Name,
                    ["scope"] = handler.Scope.ToString().ToLowerInvariant(),
                    ["description"] = handler.Description ?? "",
                    ["completion"] = completion,
                    ["aliases"] = aliases,
                    ["default_timeout_ms"] = handler.DefaultTimeoutMs > 0 ? handler.DefaultTimeoutMs : (object)null,
                    ["allow_connection_retry"] = handler.AllowConnectionRetry,
                    ["params"] = paramDescs,
                });

                versionParts.Add(
                    $"{handler.Name}|{completion}|{string.Join(",", aliases)}|{handler.DefaultTimeoutMs}|{string.Join("|", paramDescs)}");

                foreach (var alias in aliases)
                {
                    if (!string.IsNullOrEmpty(alias))
                        aliasToCommand[alias] = handler.Name;
                }
            }

            return (commands, versionParts, aliasToCommand);
        }

        private static string ComputeVersion(IEnumerable<string> parts)
        {
            var joined = string.Join("\n", parts);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return hex.Substring(0, 12);
        }
    }
}
