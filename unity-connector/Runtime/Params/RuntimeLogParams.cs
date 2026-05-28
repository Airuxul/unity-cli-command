namespace UnityCliConnector.Params
{
    public sealed class RuntimeLogParams
    {
        [CliParam(Description = "content to print")]
        public string Message { get; set; } = "runtime log";

        [CliParam(Description = "optional log tag")]
        public string Tag { get; set; }
    }
}
