using Tornado.Models;

namespace Tornado.Services;

public sealed class ClusterSnapshotCache
{
    private readonly object _lock = new();
    private ClusterSnapshot? _snapshot;

    public ClusterSnapshot? GetSnapshot()
    {
        lock (_lock)
        {
            return _snapshot;
        }
    }

    public void SetSnapshot(ClusterSnapshot snapshot)
    {
        lock (_lock)
        {
            _snapshot = snapshot;
        }
    }
}
