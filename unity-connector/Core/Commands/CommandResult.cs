namespace UnityCliConnector.Commands
{
    /// <summary>
    /// Unified command success payload.
    /// Use message for human-readable status and payload only when business data is needed.
    /// </summary>
    public sealed class CommandResult
    {
        public string Code { get; set; } = "ok";
        public string Message { get; set; } = "ok";
        public object Payload { get; set; }

        public static CommandResult Ok(string message = "ok", object payload = null) =>
            new()
            {
                Code = "ok",
                Message = string.IsNullOrWhiteSpace(message) ? "ok" : message,
                Payload = payload,
            };
    }
}
