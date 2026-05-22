using System.Collections.Generic;

namespace UnityCliConnector
{
    public sealed class CommandRequest
    {
        public string Command { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public string RequestId { get; set; }
        public string Endpoint { get; set; } = "editor";
    }

    public sealed class CommandResult
    {
        public bool Ok { get; set; }
        public object Data { get; set; }
        public string Error { get; set; }
        public string RequestId { get; set; }

        public static CommandResult Success(object data, string requestId = null) =>
            new() { Ok = true, Data = data, RequestId = requestId };

        public static CommandResult Fail(string error, string requestId = null) =>
            new() { Ok = false, Error = error, RequestId = requestId };
    }

    public sealed class JobAccepted
    {
        public string JobId { get; set; }
        public string Completion { get; set; }
        public string RequestId { get; set; }
    }

    public enum JobStatus
    {
        Pending,
        Running,
        Succeeded,
        Failed,
        Orphaned,
    }

    [System.Serializable]
    public sealed class JobRecord
    {
        public string Id;
        public string Command;
        public string RequestId;
        public JobStatus Status = JobStatus.Pending;
        public string CompletionKind;
        public string ResultJson;
        public string Error;
        public long CreatedAtUtcMs;
        public long UpdatedAtUtcMs;

        [System.NonSerialized] public object Result;
    }
}
