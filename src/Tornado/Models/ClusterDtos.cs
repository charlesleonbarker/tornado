namespace Tornado.Models;

public sealed record PodSummary(
    string Namespace,
    string Name,
    string Node,
    string Status,
    string Ready,
    int Restarts,
    string Age,
    IReadOnlyDictionary<string, string> Labels
);

public sealed record DeploymentSummary(
    string Namespace,
    string Name,
    int? Replicas,
    int? Ready,
    int? Updated,
    int? Available,
    IReadOnlyDictionary<string, string> Labels
);

public sealed record ServicePortSummary(
    int Port,
    int? TargetPort,
    int? NodePort,
    string Protocol
);

public sealed record ServiceSummary(
    string Namespace,
    string Name,
    string Type,
    string ClusterIP,
    IReadOnlyList<ServicePortSummary> Ports,
    IReadOnlyDictionary<string, string> Selector,
    IReadOnlyList<string> ExternalUrls
);

public sealed record IngressBackendSummary(
    string ServiceName,
    int? ServicePort
);

public sealed record IngressRuleSummary(
    string Host,
    string Path,
    IngressBackendSummary Backend
);

public sealed record IngressSummary(
    string Namespace,
    string Name,
    string ClassName,
    IReadOnlyList<IngressRuleSummary> Rules,
    IReadOnlyList<string> ExternalUrls
);

public sealed record NodeSummary(
    string Name,
    string InternalIp,
    string ExternalIp
);
