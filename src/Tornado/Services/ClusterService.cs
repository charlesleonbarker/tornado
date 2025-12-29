using k8s;
using k8s.Models;
using Tornado.Models;

namespace Tornado.Services;

public interface IClusterService
{
    Task<IReadOnlyList<PodSummary>> GetPodsAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceSummary>> GetServicesAsync(CancellationToken ct);
    Task<IReadOnlyList<DeploymentSummary>> GetDeploymentsAsync(CancellationToken ct);
    Task<IReadOnlyList<IngressSummary>> GetIngressesAsync(CancellationToken ct);
    Task<IReadOnlyList<NodeSummary>> GetNodesAsync(CancellationToken ct);
    Task RestartDeploymentAsync(string @namespace, string name, CancellationToken ct);
    Task<string> GetPodLogsAsync(string @namespace, string name, string? container, int? tailLines, CancellationToken ct);
}

public sealed class ClusterService : IClusterService
{
    private readonly IKubernetes _client;
    private readonly string? _namespace;

    public ClusterService(IKubernetes client, IConfiguration configuration)
    {
        _client = client;
        _namespace = configuration["ClusterNamespace"];
    }

    public async Task<IReadOnlyList<PodSummary>> GetPodsAsync(CancellationToken ct)
    {
        var list = string.IsNullOrWhiteSpace(_namespace)
            ? await _client.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: ct)
            : await _client.CoreV1.ListNamespacedPodAsync(_namespace, cancellationToken: ct);

        return list.Items.Select(pod =>
        {
            var labels = pod.Metadata?.Labels is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(pod.Metadata.Labels);
            var containerStatuses = pod.Status?.ContainerStatuses ?? new List<V1ContainerStatus>();
            var readyCount = containerStatuses.Count(c => c.Ready == true);
            var totalCount = containerStatuses.Count;
            var restarts = containerStatuses.Sum(c => c.RestartCount);

            return new PodSummary(
                Namespace: pod.Metadata?.NamespaceProperty ?? "",
                Name: pod.Metadata?.Name ?? "",
                Node: pod.Spec?.NodeName ?? "",
                Status: pod.Status?.Phase ?? "",
                Ready: $"{readyCount}/{totalCount}",
                Restarts: restarts,
                Age: FormatAge(pod.Metadata?.CreationTimestamp),
                Labels: labels
            );
        }).ToList();
    }

    public async Task<IReadOnlyList<ServiceSummary>> GetServicesAsync(CancellationToken ct)
    {
        var list = string.IsNullOrWhiteSpace(_namespace)
            ? await _client.CoreV1.ListServiceForAllNamespacesAsync(cancellationToken: ct)
            : await _client.CoreV1.ListNamespacedServiceAsync(_namespace, cancellationToken: ct);

        return list.Items.Select(service =>
        {
            var ports = service.Spec?.Ports?.Select(port => new ServicePortSummary(
                Port: port.Port,
                TargetPort: TryGetInt(port.TargetPort),
                NodePort: port.NodePort,
                Protocol: port.Protocol ?? "TCP"
            )).ToList() ?? new List<ServicePortSummary>();

            var selector = service.Spec?.Selector is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(service.Spec.Selector);

            return new ServiceSummary(
                Namespace: service.Metadata?.NamespaceProperty ?? "",
                Name: service.Metadata?.Name ?? "",
                Type: service.Spec?.Type ?? "",
                ClusterIP: service.Spec?.ClusterIP ?? "",
                Ports: ports,
                Selector: selector,
                ExternalUrls: new List<string>()
            );
        }).ToList();
    }

    public async Task<IReadOnlyList<DeploymentSummary>> GetDeploymentsAsync(CancellationToken ct)
    {
        var list = string.IsNullOrWhiteSpace(_namespace)
            ? await _client.AppsV1.ListDeploymentForAllNamespacesAsync(cancellationToken: ct)
            : await _client.AppsV1.ListNamespacedDeploymentAsync(_namespace, cancellationToken: ct);

        return list.Items.Select(deployment =>
        {
            var labels = deployment.Spec?.Template?.Metadata?.Labels is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(deployment.Spec.Template.Metadata.Labels);
            return new DeploymentSummary(
                Namespace: deployment.Metadata?.NamespaceProperty ?? "",
                Name: deployment.Metadata?.Name ?? "",
                Replicas: deployment.Spec?.Replicas,
                Ready: deployment.Status?.ReadyReplicas,
                Updated: deployment.Status?.UpdatedReplicas,
                Available: deployment.Status?.AvailableReplicas,
                Labels: labels
            );
        }).ToList();
    }

    public async Task<IReadOnlyList<IngressSummary>> GetIngressesAsync(CancellationToken ct)
    {
        var list = string.IsNullOrWhiteSpace(_namespace)
            ? await _client.NetworkingV1.ListIngressForAllNamespacesAsync(cancellationToken: ct)
            : await _client.NetworkingV1.ListNamespacedIngressAsync(_namespace, cancellationToken: ct);

        return list.Items.Select(ingress =>
        {
            var rules = new List<IngressRuleSummary>();
            if (ingress.Spec?.Rules is not null)
            {
                foreach (var rule in ingress.Spec.Rules)
                {
                    if (rule.Http?.Paths is null)
                    {
                        continue;
                    }

                    foreach (var path in rule.Http.Paths)
                    {
                        rules.Add(new IngressRuleSummary(
                            Host: rule.Host ?? "",
                            Path: path.Path ?? "/",
                            Backend: new IngressBackendSummary(
                                path.Backend?.Service?.Name ?? "",
                                path.Backend?.Service?.Port?.Number
                            )
                        ));
                    }
                }
            }

            return new IngressSummary(
                Namespace: ingress.Metadata?.NamespaceProperty ?? "",
                Name: ingress.Metadata?.Name ?? "",
                ClassName: ingress.Spec?.IngressClassName ?? "",
                Rules: rules,
                ExternalUrls: new List<string>()
            );
        }).ToList();
    }

    public async Task<IReadOnlyList<NodeSummary>> GetNodesAsync(CancellationToken ct)
    {
        var list = await _client.CoreV1.ListNodeAsync(cancellationToken: ct);

        return list.Items.Select(node =>
        {
            var internalIp = "";
            var externalIp = "";

            var addresses = node.Status?.Addresses;
            if (addresses is not null)
            {
                foreach (var address in addresses)
                {
                    if (string.Equals(address.Type, "InternalIP", StringComparison.OrdinalIgnoreCase))
                    {
                        internalIp = address.Address ?? "";
                    }
                    else if (string.Equals(address.Type, "ExternalIP", StringComparison.OrdinalIgnoreCase))
                    {
                        externalIp = address.Address ?? "";
                    }
                }
            }

            return new NodeSummary(
                Name: node.Metadata?.Name ?? "",
                InternalIp: internalIp,
                ExternalIp: externalIp
            );
        }).ToList();
    }

    public async Task RestartDeploymentAsync(string @namespace, string name, CancellationToken ct)
    {
        var payload = new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>
                        {
                            ["kubectl.kubernetes.io/restartedAt"] = DateTimeOffset.UtcNow.ToString("O")
                        }
                    }
                }
            }
        };

        var patch = new V1Patch(payload, V1Patch.PatchType.StrategicMergePatch);
        await _client.AppsV1.PatchNamespacedDeploymentAsync(patch, name, @namespace, cancellationToken: ct);
    }

    public async Task<string> GetPodLogsAsync(string @namespace, string name, string? container, int? tailLines, CancellationToken ct)
    {
        await using var stream = await _client.CoreV1.ReadNamespacedPodLogAsync(
            name,
            @namespace,
            container: container,
            tailLines: tailLines,
            cancellationToken: ct);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private static int? TryGetInt(IntstrIntOrString? value)
    {
        if (value is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(value.Value) && int.TryParse(value.Value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatAge(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return "";
        }

        var age = DateTimeOffset.UtcNow - timestamp.Value;
        if (age.TotalDays >= 1)
        {
            return $"{(int)age.TotalDays}d{age.Hours}h";
        }

        if (age.TotalHours >= 1)
        {
            return $"{(int)age.TotalHours}h{age.Minutes}m";
        }

        if (age.TotalMinutes >= 1)
        {
            return $"{(int)age.TotalMinutes}m";
        }

        return $"{(int)age.TotalSeconds}s";
    }
}
