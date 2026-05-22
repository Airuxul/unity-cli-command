namespace UnityCliConnector
{
    public interface ICompletionPolicy
    {
        string Kind { get; }
        bool TryComplete(JobRecord job, EditorStateSnapshot state, out object result, out string error);
    }

    public sealed class EditorStateSnapshot
    {
        public bool IsCompiling { get; set; }
        public bool IsPlaying { get; set; }
        public bool ReadyForTools { get; set; }
    }
}
