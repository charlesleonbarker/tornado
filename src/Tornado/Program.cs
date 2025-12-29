using k8s;
using Tornado.Components;
using Tornado.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IExampleService, ExampleService>();
builder.Services.AddSingleton<IKubernetes>(_ =>
{
    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
    {
        return new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
    }

    return new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile());
});
builder.Services.AddSingleton<IClusterService, ClusterService>();
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

app.MapGet("/cluster/pods", async (IClusterService service, CancellationToken ct) =>
    Results.Ok(await service.GetPodsAsync(ct)));

app.MapGet("/cluster/services", async (IClusterService service, CancellationToken ct) =>
    Results.Ok(await service.GetServicesAsync(ct)));

app.MapGet("/cluster/deployments", async (IClusterService service, CancellationToken ct) =>
    Results.Ok(await service.GetDeploymentsAsync(ct)));

app.MapGet("/cluster/ingresses", async (IClusterService service, CancellationToken ct) =>
    Results.Ok(await service.GetIngressesAsync(ct)));

app.MapGet("/cluster/nodes", async (IClusterService service, CancellationToken ct) =>
    Results.Ok(await service.GetNodesAsync(ct)));

app.MapGet("/cluster/pods/{namespace}/{name}/logs", async (IClusterService service, string @namespace, string name, string? container, int? tail, CancellationToken ct) =>
    Results.Text(await service.GetPodLogsAsync(@namespace, name, container, tail, ct)));

app.MapPost("/cluster/deployments/{namespace}/{name}/restart", async (IClusterService service, string @namespace, string name, CancellationToken ct) =>
{
    await service.RestartDeploymentAsync(@namespace, name, ct);
    return Results.NoContent();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
