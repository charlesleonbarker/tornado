using k8s;
using k8s.Models;
using Tornado.Models;

namespace Tornado.Services;

public interface IClusterService
{
    Task<IReadOnlyList<PodSummary>> GetPodsAsync(CancellationToken ct);
    Task<IReadOnlyList<ServiceSummary>> GetServicesAsync(CancellationToken ct);
    Task<IReadOnlyList<DeploymentSummary>> GetDeploymentsAsync(CancellationToken ct);
    Task<IReadOnlyList<WorkloadSummary>> GetWorkloadsAsync(CancellationToken ct);
    Task<IReadOnlyList<IngressSummary>> GetIngressesAsync(CancellationToken ct);
    Task<IReadOnlyList<NodeSummary>> GetNodesAsync(CancellationToken ct);
    Task<DescribeResource?> GetDescribeResourceAsync(string kind, string @namespace, string name, CancellationToken ct);
    Task<IReadOnlyList<DescribeEvent>> GetEventsAsync(string? @namespace, string uid, CancellationToken ct);
    Task<IReadOnlyList<DescribeEndpointRow>> GetServiceEndpointsAsync(string @namespace, string name, CancellationToken ct);
    Task<IReadOnlyList<DescribeReplicaSetRow>> GetDeploymentReplicaSetsAsync(string @namespace, string name, CancellationToken ct);
    Task RestartDeploymentAsync(string @namespace, string name, CancellationToken ct);
    Task RestartWorkloadAsync(string kind, string @namespace, string name, CancellationToken ct);
    Task<string> GetPodLogsAsync(string @namespace, string name, string? container, int? tailLines, CancellationToken ct);
}

public sealed class ClusterService : IClusterService
{
    private readonly IKubernetes _client;
    private readonly string? _namespace;
    private readonly string? _clusterName;

    public ClusterService(IKubernetes client, IConfiguration configuration)
    {
        _client = client;
        _namespace = configuration["ClusterNamespace"];
        _clusterName = configuration["ClusterName"];
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

    public async Task<IReadOnlyList<WorkloadSummary>> GetWorkloadsAsync(CancellationToken ct)
    {
        var deploymentsTask = string.IsNullOrWhiteSpace(_namespace)
            ? _client.AppsV1.ListDeploymentForAllNamespacesAsync(cancellationToken: ct)
            : _client.AppsV1.ListNamespacedDeploymentAsync(_namespace, cancellationToken: ct);
        var statefulSetsTask = string.IsNullOrWhiteSpace(_namespace)
            ? _client.AppsV1.ListStatefulSetForAllNamespacesAsync(cancellationToken: ct)
            : _client.AppsV1.ListNamespacedStatefulSetAsync(_namespace, cancellationToken: ct);
        var daemonSetsTask = string.IsNullOrWhiteSpace(_namespace)
            ? _client.AppsV1.ListDaemonSetForAllNamespacesAsync(cancellationToken: ct)
            : _client.AppsV1.ListNamespacedDaemonSetAsync(_namespace, cancellationToken: ct);

        await Task.WhenAll(deploymentsTask, statefulSetsTask, daemonSetsTask);

        var workloads = new List<WorkloadSummary>();
        workloads.AddRange(deploymentsTask.Result.Items.Select(deployment =>
        {
            var labels = deployment.Spec?.Template?.Metadata?.Labels is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(deployment.Spec.Template.Metadata.Labels);

            return new WorkloadSummary(
                Kind: "Deployment",
                Namespace: deployment.Metadata?.NamespaceProperty ?? "",
                Name: deployment.Metadata?.Name ?? "",
                Desired: deployment.Spec?.Replicas,
                Ready: deployment.Status?.ReadyReplicas,
                Labels: labels
            );
        }));

        workloads.AddRange(statefulSetsTask.Result.Items.Select(statefulSet =>
        {
            var labels = statefulSet.Spec?.Template?.Metadata?.Labels is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(statefulSet.Spec.Template.Metadata.Labels);

            return new WorkloadSummary(
                Kind: "StatefulSet",
                Namespace: statefulSet.Metadata?.NamespaceProperty ?? "",
                Name: statefulSet.Metadata?.Name ?? "",
                Desired: statefulSet.Spec?.Replicas,
                Ready: statefulSet.Status?.ReadyReplicas,
                Labels: labels
            );
        }));

        workloads.AddRange(daemonSetsTask.Result.Items.Select(daemonSet =>
        {
            var labels = daemonSet.Spec?.Template?.Metadata?.Labels is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(daemonSet.Spec.Template.Metadata.Labels);

            return new WorkloadSummary(
                Kind: "DaemonSet",
                Namespace: daemonSet.Metadata?.NamespaceProperty ?? "",
                Name: daemonSet.Metadata?.Name ?? "",
                Desired: daemonSet.Status?.DesiredNumberScheduled,
                Ready: daemonSet.Status?.NumberReady,
                Labels: labels
            );
        }));

        return workloads
            .OrderBy(workload => workload.Namespace, StringComparer.Ordinal)
            .ThenBy(workload => workload.Name, StringComparer.Ordinal)
            .ThenBy(workload => workload.Kind, StringComparer.Ordinal)
            .ToList();
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

    public async Task<DescribeResource?> GetDescribeResourceAsync(string kind, string @namespace, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        var normalizedKind = kind.Trim().ToLowerInvariant();
        object? resource = normalizedKind switch
        {
            "pod" => await _client.CoreV1.ReadNamespacedPodAsync(name, @namespace, cancellationToken: ct),
            "service" => await _client.CoreV1.ReadNamespacedServiceAsync(name, @namespace, cancellationToken: ct),
            "ingress" => await _client.NetworkingV1.ReadNamespacedIngressAsync(name, @namespace, cancellationToken: ct),
            "deployment" => await _client.AppsV1.ReadNamespacedDeploymentAsync(name, @namespace, cancellationToken: ct),
            "statefulset" => await _client.AppsV1.ReadNamespacedStatefulSetAsync(name, @namespace, cancellationToken: ct),
            "daemonset" => await _client.AppsV1.ReadNamespacedDaemonSetAsync(name, @namespace, cancellationToken: ct),
            "replicaset" => await _client.AppsV1.ReadNamespacedReplicaSetAsync(name, @namespace, cancellationToken: ct),
            "job" => await _client.BatchV1.ReadNamespacedJobAsync(name, @namespace, cancellationToken: ct),
            "cronjob" => await _client.BatchV1.ReadNamespacedCronJobAsync(name, @namespace, cancellationToken: ct),
            "node" => await _client.CoreV1.ReadNodeAsync(name, cancellationToken: ct),
            _ => null
        };

        if (resource is null)
        {
            return null;
        }

        return BuildDescribeResource(resource);
    }

    public async Task<IReadOnlyList<DescribeEvent>> GetEventsAsync(string? @namespace, string uid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return Array.Empty<DescribeEvent>();
        }

        var fieldSelector = $"involvedObject.uid={uid}";
        var list = string.IsNullOrWhiteSpace(@namespace)
            ? await _client.CoreV1.ListEventForAllNamespacesAsync(fieldSelector: fieldSelector, cancellationToken: ct)
            : await _client.CoreV1.ListNamespacedEventAsync(@namespace, fieldSelector: fieldSelector, cancellationToken: ct);

        return list.Items
            .Select(item =>
            {
                var time = item.LastTimestamp ?? item.EventTime ?? item.FirstTimestamp;
                var from = item.Source?.Component ?? item.ReportingComponent ?? item.Source?.Host ?? "";
                return new DescribeEvent(
                    Time: time,
                    Type: item.Type ?? "",
                    Reason: item.Reason ?? "",
                    From: from,
                    Message: item.Message ?? ""
                );
            })
            .OrderByDescending(item => item.Time ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task<IReadOnlyList<DescribeEndpointRow>> GetServiceEndpointsAsync(string @namespace, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Array.Empty<DescribeEndpointRow>();
        }

        var results = new List<DescribeEndpointRow>();
        try
        {
            var sliceList = await _client.DiscoveryV1.ListNamespacedEndpointSliceAsync(
                @namespace,
                labelSelector: $"kubernetes.io/service-name={name}",
                cancellationToken: ct);

            foreach (var slice in sliceList.Items)
            {
                var ports = slice.Ports ?? new List<Discoveryv1EndpointPort>();
                foreach (var endpoint in slice.Endpoints ?? new List<V1Endpoint>())
                {
                    var node = endpoint.NodeName ?? "";
                    var target = endpoint.TargetRef is null
                        ? ""
                        : $"{endpoint.TargetRef.Kind}/{endpoint.TargetRef.Name}";
                    var addresses = endpoint.Addresses ?? new List<string>();
                    foreach (var address in addresses)
                    {
                        if (ports.Count == 0)
                        {
                            results.Add(new DescribeEndpointRow(address, node, target, "not set", "EndpointSlice"));
                        }
                        else
                        {
                            foreach (var port in ports)
                            {
                                var portValue = port.Port?.ToString() ?? "not set";
                                results.Add(new DescribeEndpointRow(address, node, target, portValue, "EndpointSlice"));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore discovery API errors and fall back to Endpoints.
        }

        if (results.Count > 0)
        {
            return results;
        }

        var endpoints = await _client.CoreV1.ReadNamespacedEndpointsAsync(name, @namespace, cancellationToken: ct);
        foreach (var subset in endpoints.Subsets ?? new List<V1EndpointSubset>())
        {
            var ports = subset.Ports ?? new List<Corev1EndpointPort>();
            foreach (var address in subset.Addresses ?? new List<V1EndpointAddress>())
            {
                var node = address.NodeName ?? "";
                var target = address.TargetRef is null
                    ? ""
                    : $"{address.TargetRef.Kind}/{address.TargetRef.Name}";
                if (ports.Count == 0)
                {
                    results.Add(new DescribeEndpointRow(address.Ip ?? "", node, target, "not set", "Endpoints"));
                }
                else
                {
                    foreach (var port in ports)
                    {
                        results.Add(new DescribeEndpointRow(address.Ip ?? "", node, target, port.Port.ToString(), "Endpoints"));
                    }
                }
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<DescribeReplicaSetRow>> GetDeploymentReplicaSetsAsync(string @namespace, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Array.Empty<DescribeReplicaSetRow>();
        }

        var deployment = await _client.AppsV1.ReadNamespacedDeploymentAsync(name, @namespace, cancellationToken: ct);
        if (deployment.Spec?.Selector?.MatchLabels is null || deployment.Spec.Selector.MatchLabels.Count == 0)
        {
            return Array.Empty<DescribeReplicaSetRow>();
        }

        var selector = string.Join(",", deployment.Spec.Selector.MatchLabels.Select(pair => $"{pair.Key}={pair.Value}"));

        var replicaSets = await _client.AppsV1.ListNamespacedReplicaSetAsync(@namespace, labelSelector: selector, cancellationToken: ct);
        return replicaSets.Items.Select(rs =>
        {
            var revision = rs.Metadata?.Annotations is null
                ? "not set"
                : rs.Metadata.Annotations.TryGetValue("deployment.kubernetes.io/revision", out var value)
                    ? value
                    : "not set";
            return new DescribeReplicaSetRow(
                Name: rs.Metadata?.Name ?? "",
                Desired: rs.Spec?.Replicas?.ToString() ?? "not set",
                Ready: rs.Status?.ReadyReplicas?.ToString() ?? "0",
                Age: FormatAge(rs.Metadata?.CreationTimestamp),
                Revision: revision
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

    public async Task RestartWorkloadAsync(string kind, string @namespace, string name, CancellationToken ct)
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
        switch (kind.ToLowerInvariant())
        {
            case "deployment":
                await _client.AppsV1.PatchNamespacedDeploymentAsync(patch, name, @namespace, cancellationToken: ct);
                break;
            case "statefulset":
                await _client.AppsV1.PatchNamespacedStatefulSetAsync(patch, name, @namespace, cancellationToken: ct);
                break;
            case "daemonset":
                await _client.AppsV1.PatchNamespacedDaemonSetAsync(patch, name, @namespace, cancellationToken: ct);
                break;
            default:
                throw new ArgumentException($"Unsupported workload type: {kind}", nameof(kind));
        }
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

    private DescribeResource BuildDescribeResource(object resource)
    {
        var meta = resource switch
        {
            IKubernetesObject<V1ObjectMeta> obj => obj.Metadata,
            _ => null
        };

        var kind = resource switch
        {
            IKubernetesObject k8s => k8s.Kind ?? resource.GetType().Name,
            _ => resource.GetType().Name
        };

        var apiVersion = resource switch
        {
            IKubernetesObject k8s => k8s.ApiVersion ?? "",
            _ => ""
        };

        var owners = meta?.OwnerReferences?
            .Select(owner => new DescribeOwnerRef(
                Kind: owner.Kind ?? "",
                Name: owner.Name ?? "",
                Uid: owner.Uid ?? "",
                Controller: owner.Controller ?? false))
            .ToList()
            ?? new List<DescribeOwnerRef>();

        var labels = meta?.Labels is null ? new Dictionary<string, string>() : new Dictionary<string, string>(meta.Labels);
        var annotations = meta?.Annotations is null ? new Dictionary<string, string>() : new Dictionary<string, string>(meta.Annotations);

        var rawJson = System.Text.Json.JsonSerializer.Serialize(resource, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var rawYaml = KubernetesYaml.Serialize(resource);

        var status = resource switch
        {
            V1Pod pod => pod.Status?.Phase,
            V1Node node => node.Status?.Conditions?.FirstOrDefault(c => c.Type == "Ready")?.Status,
            _ => null
        };

        var reason = resource switch
        {
            V1Pod pod => pod.Status?.Reason,
            _ => null
        };

        return new DescribeResource(
            Kind: kind ?? "",
            ApiVersion: apiVersion ?? "",
            Name: meta?.Name ?? "",
            Namespace: meta?.NamespaceProperty ?? "",
            Uid: meta?.Uid ?? "",
            CreatedAt: meta?.CreationTimestamp,
            Labels: labels,
            Annotations: annotations,
            Owners: owners,
            RawJson: rawJson,
            RawYaml: rawYaml,
            Status: status,
            Reason: reason,
            Cluster: _clusterName
        );
    }
}
