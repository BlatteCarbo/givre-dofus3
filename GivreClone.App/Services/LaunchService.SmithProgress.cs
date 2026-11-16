using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    private long _smithProgressRevision;
    private readonly ConcurrentDictionary<(uint Pid, int Job), long> _smithProgressRevisions = new();
    public event Action<uint, int, long, JobProgressSnapshot>? SmithJobProgressUpdated;

    public long SmithJobProgressRevision(uint pid, int job) =>
        _smithProgressRevisions.TryGetValue((pid, job), out var revision) ? revision : 0;

    private void NoteSmithJobProgress(uint pid, int job)
    {
        if (JobProgress(pid).TryGetValue(job, out var progress))
        {
            long revision = Interlocked.Increment(ref _smithProgressRevision);
            _smithProgressRevisions[(pid, job)] = revision;
            SmithJobProgressUpdated?.Invoke(pid, job, revision, progress);
        }
    }
}
