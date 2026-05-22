using System;
using System.Collections.Generic;

namespace UnityCliConnector
{
    public sealed class CliParams
    {
        private readonly Dictionary<string, object> _values;

        public CliParams(Dictionary<string, object> values)
        {
            _values = values ?? new Dictionary<string, object>();
        }

        public bool Has(string key) => _values.ContainsKey(key);

        public string GetString(string key, string defaultValue = null)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return defaultValue;
            return raw.ToString();
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return defaultValue;
            if (raw is bool b) return b;
            return bool.TryParse(raw.ToString(), out var parsed) && parsed;
        }
    }
}
