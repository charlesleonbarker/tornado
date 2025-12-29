using System.Text.Json;
using Tornado.Components;
using Tornado.Models;
using Tornado.Services;

var builder = WebApplication.CreateBuilder(args);
var clusterNamespace = builder.Configuration["ClusterNamespace"];

builder.Services.AddSingleton<IExampleService, ExampleService>();
builder.Services.AddSingleton<IKubectlService, KubectlService>();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/example", (IExampleService service) => Results.Ok(service.GetExample()));

app.MapGet("/cluster/pods", async (IKubectlService service, CancellationToken ct) =>
    await MapKubectlJson(service, BuildClusterArgs("pods", clusterNamespace), ParsePods, ct));

app.MapGet("/cluster/services", async (IKubectlService service, CancellationToken ct) =>
    await MapKubectlJson(service, BuildClusterArgs("services", clusterNamespace), ParseServices, ct));

app.MapGet("/cluster/deployments", async (IKubectlService service, CancellationToken ct) =>
    await MapKubectlJson(service, BuildClusterArgs("deployments", clusterNamespace), ParseDeployments, ct));

app.MapGet("/cluster/ingresses", async (IKubectlService service, CancellationToken ct) =>
    await MapKubectlJson(service, BuildClusterArgs("ingress", clusterNamespace), ParseIngresses, ct));

app.MapGet("/cluster/nodes", async (IKubectlService service, CancellationToken ct) =>
    await MapKubectlJson(service, new List<string> { "get", "nodes", "-o", "json" }, ParseNodes, ct));

app.MapPost("/cluster/deployments/{namespace}/{name}/restart", async (IKubectlService service, string @namespace, string name, CancellationToken ct) =>
    await RestartDeployment(service, @namespace, name, ct));

var kubectl = app.MapGroup("/kubectl");

kubectl.MapGet("/pods", async (IKubectlService service, string? @namespace, bool allNamespaces, CancellationToken ct) =>
    await RunKubectl(service, BuildGetArgs("pods", @namespace, allNamespaces), ct));

kubectl.MapGet("/services", async (IKubectlService service, string? @namespace, bool allNamespaces, CancellationToken ct) =>
    await RunKubectl(service, BuildGetArgs("services", @namespace, allNamespaces), ct));

kubectl.MapGet("/deployments", async (IKubectlService service, string? @namespace, bool allNamespaces, CancellationToken ct) =>
    await RunKubectl(service, BuildGetArgs("deployments", @namespace, allNamespaces), ct));

kubectl.MapGet("/namespaces", async (IKubectlService service, CancellationToken ct) =>
    await RunKubectl(service, new List<string> { "get", "namespaces" }, ct));

kubectl.MapGet("/nodes", async (IKubectlService service, CancellationToken ct) =>
    await RunKubectl(service, new List<string> { "get", "nodes" }, ct));

kubectl.MapGet("/describe/pod/{name}", async (IKubectlService service, string name, string? @namespace, CancellationToken ct) =>
    await RunKubectl(service, BuildDescribeArgs("pod", name, @namespace), ct));

kubectl.MapGet("/logs/{pod}", async (IKubectlService service, string pod, string? @namespace, string? container, int? tail, CancellationToken ct) =>
    await RunKubectl(service, BuildLogsArgs(pod, @namespace, container, tail), ct));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static List<string> BuildGetArgs(string resource, string? @namespace, bool allNamespaces)
{
    var args = new List<string> { "get", resource };

    if (allNamespaces)
    {
        args.Add("-A");
        return args;
    }

    if (!string.IsNullOrWhiteSpace(@namespace))
    {
        args.Add("-n");
        args.Add(@namespace);
    }

    return args;
}

static List<string> BuildDescribeArgs(string resource, string name, string? @namespace)
{
    var args = new List<string> { "describe", resource, name };

    if (!string.IsNullOrWhiteSpace(@namespace))
    {
        args.Add("-n");
        args.Add(@namespace);
    }

    return args;
}

static List<string> BuildLogsArgs(string pod, string? @namespace, string? container, int? tail)
{
    var args = new List<string> { "logs", pod };

    if (!string.IsNullOrWhiteSpace(@namespace))
    {
        args.Add("-n");
        args.Add(@namespace);
    }

    if (!string.IsNullOrWhiteSpace(container))
    {
        args.Add("-c");
        args.Add(container);
    }

    if (tail is > 0)
    {
        args.Add("--tail");
        args.Add(tail.Value.ToString());
    }

    return args;
}

static async Task<IResult> RunKubectl(IKubectlService service, List<string> args, CancellationToken ct)
{
    var result = await service.RunAsync(args, ct);
    var status = result.ExitCode == 0 ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
    return Results.Json(result, statusCode: status);
}

static async Task<IResult> RestartDeployment(IKubectlService service, string @namespace, string name, CancellationToken ct)
{
    var args = new List<string> { "rollout", "restart", $"deployment/{name}", "-n", @namespace };
    return await RunKubectl(service, args, ct);
}

static List<string> BuildClusterArgs(string resource, string? clusterNamespace)
{
    var args = new List<string> { "get", resource };

    if (!string.IsNullOrWhiteSpace(clusterNamespace))
    {
        args.Add("-n");
        args.Add(clusterNamespace);
    }
    else
    {
        args.Add("-A");
    }

    args.Add("-o");
    args.Add("json");
    return args;
}

static async Task<IResult> MapKubectlJson(
    IKubectlService service,
    List<string> args,
    Func<JsonElement, object> mapper,
    CancellationToken ct)
{
    var result = await service.RunAsync(args, ct);
    if (result.ExitCode != 0)
    {
        return Results.Json(result, statusCode: StatusCodes.Status500InternalServerError);
    }

    try
    {
        using var document = JsonDocument.Parse(result.StandardOutput);
        var data = mapper(document.RootElement);
        return Results.Json(data);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
}

static List<PodSummary> ParsePods(JsonElement root)
{
    var pods = new List<PodSummary>();

    if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
    {
        return pods;
    }

    foreach (var item in items.EnumerateArray())
    {
        var metadata = item.GetProperty("metadata");
        var status = item.GetProperty("status");
        var spec = item.GetProperty("spec");

        var labels = ReadLabels(metadata, "labels");
        var createdAt = metadata.TryGetProperty("creationTimestamp", out var created) &&
                        DateTimeOffset.TryParse(created.GetString(), out var createdAtValue)
            ? createdAtValue
            : (DateTimeOffset?)null;

        var containerStatuses = status.TryGetProperty("containerStatuses", out var cs) &&
                                cs.ValueKind == JsonValueKind.Array
            ? cs.EnumerateArray().ToList()
            : new List<JsonElement>();

        var readyCount = containerStatuses.Count(c => c.TryGetProperty("ready", out var readyProp) && readyProp.GetBoolean());
        var totalCount = containerStatuses.Count;
        var restarts = containerStatuses.Sum(c => c.TryGetProperty("restartCount", out var rc) ? rc.GetInt32() : 0);

        pods.Add(new PodSummary(
            Namespace: metadata.GetProperty("namespace").GetString() ?? "",
            Name: metadata.GetProperty("name").GetString() ?? "",
            Node: spec.TryGetProperty("nodeName", out var node) ? node.GetString() ?? "" : "",
            Status: status.TryGetProperty("phase", out var phase) ? phase.GetString() ?? "" : "",
            Ready: $"{readyCount}/{totalCount}",
            Restarts: restarts,
            Age: FormatAge(createdAt),
            Labels: labels
        ));
    }

    return pods;
}

static List<ServiceSummary> ParseServices(JsonElement root)
{
    var services = new List<ServiceSummary>();

    if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
    {
        return services;
    }

    foreach (var item in items.EnumerateArray())
    {
        var metadata = item.GetProperty("metadata");
        var spec = item.GetProperty("spec");

        var selector = ReadLabels(spec, "selector");
        var ports = new List<ServicePortSummary>();

        if (spec.TryGetProperty("ports", out var portsElement) && portsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var portElement in portsElement.EnumerateArray())
            {
                ports.Add(new ServicePortSummary(
                    Port: portElement.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 0,
                    TargetPort: TryGetInt(portElement, "targetPort"),
                    NodePort: portElement.TryGetProperty("nodePort", out var nodePort) ? nodePort.GetInt32() : (int?)null,
                    Protocol: portElement.TryGetProperty("protocol", out var protocol) ? protocol.GetString() ?? "TCP" : "TCP"
                ));
            }
        }

        var externalUrls = new List<string>();
        var type = spec.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";

        if (spec.TryGetProperty("externalIPs", out var externalIps) && externalIps.ValueKind == JsonValueKind.Array)
        {
            externalUrls.AddRange(externalIps.EnumerateArray()
                .Select(ip => ip.GetString())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .Select(ip => $"http://{ip}"));
        }

        if (item.TryGetProperty("status", out var status) &&
            status.TryGetProperty("loadBalancer", out var lb) &&
            lb.TryGetProperty("ingress", out var ingress) &&
            ingress.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in ingress.EnumerateArray())
            {
                var host = entry.TryGetProperty("hostname", out var hostname) ? hostname.GetString() :
                    entry.TryGetProperty("ip", out var ip) ? ip.GetString() : null;
                if (!string.IsNullOrWhiteSpace(host))
                {
                    externalUrls.Add($"http://{host}");
                }
            }
        }

        services.Add(new ServiceSummary(
            Namespace: metadata.GetProperty("namespace").GetString() ?? "",
            Name: metadata.GetProperty("name").GetString() ?? "",
            Type: type,
            ClusterIP: spec.TryGetProperty("clusterIP", out var clusterIp) ? clusterIp.GetString() ?? "" : "",
            Ports: ports,
            Selector: selector,
            ExternalUrls: externalUrls
        ));
    }

    return services;
}

static List<DeploymentSummary> ParseDeployments(JsonElement root)
{
    var deployments = new List<DeploymentSummary>();

    if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
    {
        return deployments;
    }

    foreach (var item in items.EnumerateArray())
    {
        var metadata = item.GetProperty("metadata");
        var spec = item.GetProperty("spec");
        var status = item.TryGetProperty("status", out var statusProp) ? statusProp : default;

        var labels = spec.TryGetProperty("template", out var template) &&
                     template.TryGetProperty("metadata", out var templateMeta)
            ? ReadLabels(templateMeta, "labels")
            : new Dictionary<string, string>();

        deployments.Add(new DeploymentSummary(
            Namespace: metadata.GetProperty("namespace").GetString() ?? "",
            Name: metadata.GetProperty("name").GetString() ?? "",
            Replicas: spec.TryGetProperty("replicas", out var replicas) ? replicas.GetInt32() : (int?)null,
            Ready: status.ValueKind != JsonValueKind.Undefined &&
                   status.TryGetProperty("readyReplicas", out var ready) ? ready.GetInt32() : (int?)null,
            Updated: status.ValueKind != JsonValueKind.Undefined &&
                     status.TryGetProperty("updatedReplicas", out var updated) ? updated.GetInt32() : (int?)null,
            Available: status.ValueKind != JsonValueKind.Undefined &&
                       status.TryGetProperty("availableReplicas", out var available) ? available.GetInt32() : (int?)null,
            Labels: labels
        ));
    }

    return deployments;
}

static List<IngressSummary> ParseIngresses(JsonElement root)
{
    var ingresses = new List<IngressSummary>();

    if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
    {
        return ingresses;
    }

    foreach (var item in items.EnumerateArray())
    {
        var metadata = item.GetProperty("metadata");
        var spec = item.TryGetProperty("spec", out var specProp) ? specProp : default;
        var status = item.TryGetProperty("status", out var statusProp) ? statusProp : default;

        var className = spec.ValueKind != JsonValueKind.Undefined &&
                        spec.TryGetProperty("ingressClassName", out var classProp)
            ? classProp.GetString() ?? ""
            : "";

        var rules = new List<IngressRuleSummary>();
        if (spec.ValueKind != JsonValueKind.Undefined &&
            spec.TryGetProperty("rules", out var rulesProp) &&
            rulesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in rulesProp.EnumerateArray())
            {
                var host = rule.TryGetProperty("host", out var hostProp) ? hostProp.GetString() ?? "" : "";
                if (rule.TryGetProperty("http", out var http) &&
                    http.TryGetProperty("paths", out var paths) &&
                    paths.ValueKind == JsonValueKind.Array)
                {
                    foreach (var path in paths.EnumerateArray())
                    {
                        var pathValue = path.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "/" : "/";
                        var backend = path.TryGetProperty("backend", out var backendProp) ? backendProp : default;
                        var serviceName = "";
                        int? servicePort = null;

                        if (backend.ValueKind != JsonValueKind.Undefined &&
                            backend.TryGetProperty("service", out var serviceProp))
                        {
                            if (serviceProp.TryGetProperty("name", out var nameProp))
                            {
                                serviceName = nameProp.GetString() ?? "";
                            }

                            if (serviceProp.TryGetProperty("port", out var portProp))
                            {
                                if (portProp.TryGetProperty("number", out var numberProp))
                                {
                                    servicePort = numberProp.GetInt32();
                                }
                                else if (portProp.TryGetProperty("name", out var namePortProp))
                                {
                                    if (int.TryParse(namePortProp.GetString(), out var parsed))
                                    {
                                        servicePort = parsed;
                                    }
                                }
                            }
                        }

                        rules.Add(new IngressRuleSummary(
                            Host: host,
                            Path: pathValue,
                            Backend: new IngressBackendSummary(serviceName, servicePort)
                        ));
                    }
                }
            }
        }

        var externalUrls = new List<string>();
        if (status.ValueKind != JsonValueKind.Undefined &&
            status.TryGetProperty("loadBalancer", out var lb) &&
            lb.TryGetProperty("ingress", out var ingress) &&
            ingress.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in ingress.EnumerateArray())
            {
                var host = entry.TryGetProperty("hostname", out var hostname) ? hostname.GetString() :
                    entry.TryGetProperty("ip", out var ip) ? ip.GetString() : null;
                if (!string.IsNullOrWhiteSpace(host))
                {
                    externalUrls.Add($"http://{host}");
                }
            }
        }

        ingresses.Add(new IngressSummary(
            Namespace: metadata.GetProperty("namespace").GetString() ?? "",
            Name: metadata.GetProperty("name").GetString() ?? "",
            ClassName: className,
            Rules: rules,
            ExternalUrls: externalUrls
        ));
    }

    return ingresses;
}

static List<NodeSummary> ParseNodes(JsonElement root)
{
    var nodes = new List<NodeSummary>();

    if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
    {
        return nodes;
    }

    foreach (var item in items.EnumerateArray())
    {
        var metadata = item.GetProperty("metadata");
        var status = item.TryGetProperty("status", out var statusProp) ? statusProp : default;
        var internalIp = "";
        var externalIp = "";

        if (status.ValueKind != JsonValueKind.Undefined &&
            status.TryGetProperty("addresses", out var addresses) &&
            addresses.ValueKind == JsonValueKind.Array)
        {
            foreach (var address in addresses.EnumerateArray())
            {
                var type = address.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                var value = address.TryGetProperty("address", out var addrProp) ? addrProp.GetString() : null;

                if (string.Equals(type, "InternalIP", StringComparison.OrdinalIgnoreCase))
                {
                    internalIp = value ?? "";
                }
                else if (string.Equals(type, "ExternalIP", StringComparison.OrdinalIgnoreCase))
                {
                    externalIp = value ?? "";
                }
            }
        }

        nodes.Add(new NodeSummary(
            Name: metadata.GetProperty("name").GetString() ?? "",
            InternalIp: internalIp,
            ExternalIp: externalIp
        ));
    }

    return nodes;
}

static Dictionary<string, string> ReadLabels(JsonElement element, string propertyName)
{
    var labels = new Dictionary<string, string>();

    if (!element.TryGetProperty(propertyName, out var labelsElement) ||
        labelsElement.ValueKind != JsonValueKind.Object)
    {
        return labels;
    }

    foreach (var label in labelsElement.EnumerateObject())
    {
        labels[label.Name] = label.Value.GetString() ?? "";
    }

    return labels;
}

static int? TryGetInt(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var value))
    {
        return null;
    }

    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
    {
        return number;
    }

    if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
    {
        return parsed;
    }

    return null;
}

static string FormatAge(DateTimeOffset? timestamp)
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
