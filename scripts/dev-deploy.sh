#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
IMAGE_TAG_HOST="localhost:5001/tornado:dev"
IMAGE_TAG_CLUSTER="k3d-tornado-reg:5000/tornado:dev"
CLUSTER_NAME="tornado"
REGISTRY_NAME="tornado-reg"
REGISTRY_PORT="5001"

ensure_registry() {
    local list_output

    if list_output=$(k3d registry list --no-headers 2>/dev/null); then
        if echo "${list_output}" | awk '{print $1}' | grep -qx "${REGISTRY_NAME}"; then
            return 0
        fi
    fi

    echo "Creating k3d registry ${REGISTRY_NAME} on port ${REGISTRY_PORT}..."
    if ! create_output=$(k3d registry create "${REGISTRY_NAME}" --port "${REGISTRY_PORT}" 2>&1); then
        if echo "${create_output}" | grep -qi "already exists"; then
            echo "Registry ${REGISTRY_NAME} already exists."
            return 0
        fi

        echo "${create_output}" >&2
        exit 1
    fi
}

ensure_registry_connected() {
    if k3d cluster list --no-headers 2>/dev/null | awk '{print $1}' | grep -qx "${CLUSTER_NAME}"; then
        return 0
    fi

    echo "Cluster ${CLUSTER_NAME} not found. Create it with:" >&2
    echo "k3d cluster create ${CLUSTER_NAME} --agents 1 --port \"80:80@loadbalancer\" --registry-use k3d-${REGISTRY_NAME}:${REGISTRY_PORT}" >&2
    exit 1
}

ensure_registry
ensure_registry_connected

cd "${REPO_ROOT}"

echo "Building image ${IMAGE_TAG_HOST}..."
docker build -t "${IMAGE_TAG_HOST}" .

echo "Pushing image ${IMAGE_TAG_HOST}..."
docker push "${IMAGE_TAG_HOST}"

echo "Applying manifests..."
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s

echo "Rolling deployment..."
kubectl -n tornado rollout restart deployment/tornado

kubectl -n tornado rollout status deployment/tornado --timeout=120s
