# Tornado

Tornado is a minimal ASP.NET 8 + Blazor Server dashboard for inspecting a
Kubernetes cluster. It uses the official Kubernetes .NET client to read
cluster data and renders structured tables for services, deployments,
ingresses, and pods with actions like restarting deployments or opening
services.

## Features

- Live cluster summaries for services, deployments, ingresses, and pods
- Cross-table correlation using Kubernetes label selectors
- Deployment restart action from the UI
- Open service and ingress endpoints in new tabs
- Optional namespace scoping via environment variable
- K8s-ready manifests and a local `k3d` dev deploy script

## Project layout

- `src/Tornado/`
  - `Program.cs`: ASP.NET minimal API + Razor components setup and cluster APIs.
  - `Components/Pages/Index.razor`: dashboard UI and table logic.
  - `Components/Layout/MainLayout.razor`: header and nav.
  - `Models/ClusterDtos.cs`: DTOs for pods, services, deployments, ingresses, nodes.
- `Services/ClusterService.cs`: calls the Kubernetes API via the .NET client.
  - `wwwroot/css/site.css`: custom UI styling.
- `k8s/`: Kubernetes manifests (namespace, deployment, service, ingress, RBAC, config).
- `scripts/dev-deploy.sh`: local build/push/apply script for k3d.
- `Dockerfile`: container build for the app.
- `global.json`: pins the .NET SDK used by the repo.

## Requirements

- .NET SDK 8.x
- Docker (for container builds)
- Kubernetes API access (in cluster via service account, or local kubeconfig)
- A Kubernetes cluster (k3d used by the dev deploy script)

## Local development

```bash
dotnet restore
dotnet run --project src/Tornado/Tornado.csproj
```

The app will serve the dashboard at `/` and Swagger at `/swagger`.

## Kubernetes deployment

Manifests live in `k8s/` and assume:

- namespace: `tornado`
- service: `tornado`
- container port: 8080

Apply them:

```bash
kubectl apply -f k8s/
```

If you use the dev deploy script, see below.

## Dev deploy (k3d)

The script builds the Docker image, pushes to a local registry, applies
manifests, and restarts the deployment:

```bash
./scripts/dev-deploy.sh
```

The script expects:

- a running Docker daemon
- `k3d` and `kubectl` installed
- a k3d cluster named `tornado` with a registry `tornado-reg` on port 5001

## Configuration

These values are read from environment variables (or `k8s/configmap.yaml`):

- `ASPNETCORE_ENVIRONMENT`: `Development` or `Production`
- `ASPNETCORE_URLS`: default `http://+:8080`
- `SelfBaseUrl`: internal base URL for the app to call itself (used in k8s)
- `ClusterNamespace`: if set, restricts all cluster queries to that namespace.
  If empty, the dashboard loads all namespaces.

## API endpoints

Cluster summary APIs (return JSON):

- `GET /cluster/pods`
- `GET /cluster/services`
- `GET /cluster/deployments`
- `GET /cluster/ingresses`
- `GET /cluster/nodes`

Actions:

- `POST /cluster/deployments/{namespace}/{name}/restart`

Health and example:

- `GET /health`
- `GET /example`

## UI behavior

- The dashboard loads cluster data on page load.
- Services correlate to deployments/pods using label selectors.
- “Open” buttons:
  - Services: uses node IP + nodePort (when available).
  - Ingresses: uses the rule host/path when available.

## Notes on security and RBAC

The backend calls the Kubernetes API, so the pod needs RBAC permissions for:

- list/get pods, services, deployments, ingresses, nodes
- get pod logs
- patch/update deployments for rollout restart

See `k8s/rbac.yaml` and adjust to your cluster policies.

## License

MIT - see `LICENSE`.
