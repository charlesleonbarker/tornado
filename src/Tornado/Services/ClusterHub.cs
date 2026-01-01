using Microsoft.AspNetCore.SignalR;
using Tornado.Models;

namespace Tornado.Services;

public sealed class ClusterHub(ClusterSnapshotCache cache) : Hub
{
    public const string HubRoute = "/hubs/cluster";
    public const string ClusterUpdatedEvent = "clusterUpdated";

    private static int _connections;

    public static int ConnectionCount => _connections;

    public override Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connections);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connections);
        return base.OnDisconnectedAsync(exception);
    }

    public ClusterSnapshot? GetSnapshot()
        => cache.GetSnapshot();
}
