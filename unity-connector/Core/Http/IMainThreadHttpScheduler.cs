using System;
using System.Collections.Generic;

namespace UnityCliConnector.Http
{
    /// <summary>Routes HTTP work to the host main thread (Editor update / play-mode Update).</summary>
    public interface IMainThreadHttpScheduler : ICommandScheduler
    {
        void ScheduleCatalog(Action<int, Dictionary<string, object>> writeJson);

        void ScheduleCommandStatus(string commandId, Action<int, Dictionary<string, object>> writeJson);
    }
}
