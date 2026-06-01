using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace UnityCliConnector.Http
{
    public static class HttpProbe
    {
        public static bool TryGetHealth(
            string host,
            int port,
            int timeoutMs,
            out string body)
        {
            body = null;
            var url = $"http://{host}:{port}/health";
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.KeepAlive = false;

                using var response = (HttpWebResponse)request.GetResponse();
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    return false;

                using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null);
                body = reader.ReadToEnd();
                return !string.IsNullOrEmpty(body);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryValidateHealth(
            string body,
            string expectedHost,
            int? expectedBuild = null,
            string expectedSessionId = null)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;

            var data = ConnectorJson.ParseObject(body);
            if (!ReadBool(data, "ok"))
                return false;

            if (!string.IsNullOrEmpty(expectedHost))
            {
                var host = data.TryGetValue("host", out var h) ? h?.ToString() : null;
                if (!string.Equals(host, expectedHost, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (expectedBuild.HasValue)
            {
                if (!data.TryGetValue("connector_build", out var buildObj))
                    return false;
                if (!TryReadInt(buildObj, out var build) || build != expectedBuild.Value)
                    return false;
            }

            if (!string.IsNullOrEmpty(expectedSessionId))
            {
                var session = data.TryGetValue("session_id", out var s) ? s?.ToString() : null;
                if (!string.Equals(session, expectedSessionId, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        public static string DescribeValidationFailure(
            string body,
            string expectedHost,
            int? expectedBuild = null,
            string expectedSessionId = null)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "empty health body";

            var data = ConnectorJson.ParseObject(body);
            if (!ReadBool(data, "ok"))
                return "ok is not true";

            if (!string.IsNullOrEmpty(expectedHost))
            {
                var host = data.TryGetValue("host", out var h) ? h?.ToString() : null;
                if (!string.Equals(host, expectedHost, StringComparison.OrdinalIgnoreCase))
                    return $"host mismatch (got {host ?? "null"}, expected {expectedHost})";
            }

            if (expectedBuild.HasValue)
            {
                if (!data.TryGetValue("connector_build", out var buildObj)
                    || !TryReadInt(buildObj, out var build)
                    || build != expectedBuild.Value)
                {
                    return $"connector_build mismatch (expected {expectedBuild.Value})";
                }
            }

            if (!string.IsNullOrEmpty(expectedSessionId))
            {
                var session = data.TryGetValue("session_id", out var s) ? s?.ToString() : null;
                if (!string.Equals(session, expectedSessionId, StringComparison.Ordinal))
                    return $"session_id mismatch (stale listener?)";
            }

            return "unknown validation failure";
        }

        private static bool ReadBool(IReadOnlyDictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return false;

            return value switch
            {
                bool b => b,
                string s when bool.TryParse(s, out var parsed) => parsed,
                _ => false,
            };
        }

        private static bool TryReadInt(object value, out int result)
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case long l when l >= int.MinValue && l <= int.MaxValue:
                    result = (int)l;
                    return true;
                case string s when int.TryParse(s, out var parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }
    }
}
