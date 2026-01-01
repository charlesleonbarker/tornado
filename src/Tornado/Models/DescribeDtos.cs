namespace Tornado.Models;

public sealed record DescribeOwnerRef(
    string Kind,
    string Name,
    string Uid,
    bool Controller
);

public sealed record DescribeResource(
    string Kind,
    string ApiVersion,
    string Name,
    string Namespace,
    string Uid,
    DateTimeOffset? CreatedAt,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Annotations,
    IReadOnlyList<DescribeOwnerRef> Owners,
    string RawJson,
    string RawYaml,
    string? Status,
    string? Reason,
    string? Cluster
);

public sealed record DescribeEvent(
    DateTimeOffset? Time,
    string Type,
    string Reason,
    string From,
    string Message
);

public sealed record DescribeEndpointRow(
    string Address,
    string Node,
    string Target,
    string Port,
    string Source
);

public sealed record DescribeReplicaSetRow(
    string Name,
    string Desired,
    string Ready,
    string Age,
    string Revision
);

public sealed record ResourceRef(
    string Kind,
    string Namespace,
    string Name
);
