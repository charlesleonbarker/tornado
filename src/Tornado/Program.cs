using Tornado.Components;
using Tornado.Services;

var builder = WebApplication.CreateBuilder(args);

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
