namespace UnityCliConnector.Completion
{
    public sealed class EnterPlayModePolicy : ICompletionPolicy
    {
        public string Kind => CommandJobCatalog.CompletionEnterPlay;

        public bool TryComplete(JobRecord job, EditorStateSnapshot state, out object result, out string error)
        {
            result = null;
            error = null;

            if (state.IsPlaying)
            {
                result = new System.Collections.Generic.Dictionary<string, object> { ["is_playing"] = true };
                return true;
            }

            if (job.Status == JobStatus.Pending)
                job.Status = JobStatus.Running;

            return false;
        }
    }
}
