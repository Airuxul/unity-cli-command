using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector
{
    [InitializeOnLoad]
    public static class JobManager
    {
        private const string SessionKey = "UnityCliConnector.Jobs";
        private const int OrphanTimeoutMs = 20000;

        private static readonly Dictionary<string, ICompletionPolicy> Policies = new()
        {
            { CommandJobCatalog.CompletionCompilation, new Completion.CompilationPolicy() },
            { CommandJobCatalog.CompletionEnterPlay, new Completion.EnterPlayModePolicy() },
            { CommandJobCatalog.CompletionExitPlay, new Completion.ExitPlayModePolicy() },
        };

        private static Dictionary<string, JobRecord> _jobs = new();

        static JobManager()
        {
            Load();
            EditorApplication.update += Tick;
        }

        public static IReadOnlyDictionary<string, JobRecord> AllJobs => _jobs;

        public static JobRecord Get(string id) =>
            id != null && _jobs.TryGetValue(id, out var job) ? job : null;

        public static JobRecord Create(string command, string completionKind, string requestId)
        {
            var job = new JobRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Command = command,
                RequestId = requestId,
                CompletionKind = completionKind,
                Status = JobStatus.Pending,
                CreatedAtUtcMs = UtcNowMs(),
                UpdatedAtUtcMs = UtcNowMs(),
            };
            _jobs[job.Id] = job;
            Save();
            return job;
        }

        public static void Fail(string id, string error)
        {
            if (!_jobs.TryGetValue(id, out var job))
                return;
            job.Status = JobStatus.Failed;
            job.Error = error;
            job.UpdatedAtUtcMs = UtcNowMs();
            Save();
        }

        public static void Succeed(string id, object result)
        {
            if (!_jobs.TryGetValue(id, out var job))
                return;
            job.Status = JobStatus.Succeeded;
            job.Result = result;
            job.ResultJson = result != null ? SimpleJson.Serialize(result) : "";
            job.UpdatedAtUtcMs = UtcNowMs();
            Save();
        }

        private static void Tick()
        {
            if (_jobs.Count == 0)
                return;

            var state = EditorStateProvider.Capture();
            var now = UtcNowMs();

            foreach (var job in _jobs.Values.ToList())
            {
                if (job.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Orphaned)
                    continue;

                if (now - job.UpdatedAtUtcMs > OrphanTimeoutMs)
                {
                    job.Status = JobStatus.Orphaned;
                    job.Error = "Job timed out without progress (20s).";
                    job.UpdatedAtUtcMs = now;
                    Save();
                    continue;
                }

                if (!Policies.TryGetValue(job.CompletionKind ?? "", out var policy))
                {
                    job.Status = JobStatus.Failed;
                    job.Error = $"No completion policy for '{job.CompletionKind}'.";
                    job.UpdatedAtUtcMs = now;
                    Save();
                    continue;
                }

                if (policy.TryComplete(job, state, out var result, out var error))
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        job.Status = JobStatus.Failed;
                        job.Error = error;
                    }
                    else
                    {
                        job.Status = JobStatus.Succeeded;
                        job.Result = result;
                    }

                    job.UpdatedAtUtcMs = now;
                    Save();
                }
                else if (job.Status == JobStatus.Pending)
                {
                    job.Status = JobStatus.Running;
                    job.UpdatedAtUtcMs = now;
                    Save();
                }
            }
        }

        private static void Load()
        {
            var json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json))
            {
                _jobs = new Dictionary<string, JobRecord>();
                return;
            }

            try
            {
                var wrapper = JsonUtility.FromJson<JobListWrapper>(json);
                _jobs = new Dictionary<string, JobRecord>();
                if (wrapper?.Items != null)
                {
                    foreach (var item in wrapper.Items)
                    {
                        if (!string.IsNullOrEmpty(item?.Id))
                            _jobs[item.Id] = item;
                    }
                }
            }
            catch
            {
                _jobs = new Dictionary<string, JobRecord>();
            }
        }

        private static void Save()
        {
            if (!MainThread.IsCurrent)
            {
                Debug.LogWarning("[unity-connector] JobManager.Save skipped (not main thread).");
                return;
            }

            var wrapper = new JobListWrapper { Items = _jobs.Values.ToList() };
            SessionState.SetString(SessionKey, JsonUtility.ToJson(wrapper));
        }

        private static long UtcNowMs() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        [Serializable]
        private class JobListWrapper
        {
            public List<JobRecord> Items = new();
        }
    }
}
