using System.Collections.Generic;

namespace UnityCliConnector.Http
{
    /// <summary>Optional host-specific fields for GET /health.</summary>
    public interface IHealthMetadataProvider
    {
        void AppendHealth(Dictionary<string, object> payload);
    }
}
