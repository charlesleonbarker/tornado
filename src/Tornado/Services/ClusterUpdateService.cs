using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Tornado.Models;

namespace Tornado.Services;

public sealed class ClusterUpdateService(
    IClusterService clusterService,
    ClusterSnapshotCache cache,
    IHubContext<ClusterHub> hubContext,
    ILogger<ClusterUpdateService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string? _lastPayloadHash;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await BuildSnapshotAsync(stoppingToken);
                var payloadHash = ComputePayloadHash(snapshot);
                if (payloadHash == _lastPayloadHash)
                {
                    continue;
                }

                _lastPayloadHash = payloadHash;
                cache.SetSnapshot(snapshot);
                await hubContext.Clients.All.SendAsync(ClusterHub.ClusterUpdatedEvent, snapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cluster update broadcast failed.");
            }

            var delay = ClusterHub.ConnectionCount > 0
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(30);

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<ClusterSnapshot> BuildSnapshotAsync(CancellationToken ct)
    {
        var podsTask = clusterService.GetPodsAsync(ct);
        var servicesTask = clusterService.GetServicesAsync(ct);
        var workloadsTask = clusterService.GetWorkloadsAsync(ct);
        var ingressesTask = clusterService.GetIngressesAsync(ct);
        var nodesTask = clusterService.GetNodesAsync(ct);

        await Task.WhenAll(podsTask, servicesTask, workloadsTask, ingressesTask, nodesTask);

        return new ClusterSnapshot(
            Pods: podsTask.Result,
            Services: servicesTask.Result,
            Workloads: workloadsTask.Result,
            Ingresses: ingressesTask.Result,
            Nodes: nodesTask.Result
        );
    }

    private static string ComputePayloadHash(ClusterSnapshot snapshot)
    {
        var payload = JsonSerializer.Serialize(snapshot, SerializerOptions);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }
}
