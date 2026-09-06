# One-time Azure & GitHub setup

This is the checklist to run **once**, before the `build-and-deploy.yml` workflow
will succeed. It provisions the Azure resources and wires up GitHub Actions to
deploy to them without ever storing a secret.

Run these with the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
(`az`) logged in as yourself (`az login`), from a terminal — either on your own
machine, or from Cloud Shell at https://portal.azure.com. You need an Azure
subscription with **Owner** (or User Access Administrator + Contributor)
permissions, and a GitHub repo this code has been pushed to.

## 1. Variables — fill these in once, reuse everywhere below

```bash
LOCATION="eastus"                       # pick the Azure region closest to you
RESOURCE_GROUP="rg-appointments"
ACR_NAME="appointmentsacr$RANDOM"       # must be globally unique, letters/numbers only
AKS_CLUSTER_NAME="aks-appointments"
GITHUB_ORG="<your-github-username-or-org>"
GITHUB_REPO="<your-repo-name>"
```

## 2. Resource group, Container Registry, AKS cluster

```bash
az group create --name "$RESOURCE_GROUP" --location "$LOCATION"

az acr create --resource-group "$RESOURCE_GROUP" --name "$ACR_NAME" --sku Basic

# A small, low-cost cluster to start (1 node, B2s). Resize later as needed.
az aks create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$AKS_CLUSTER_NAME" \
  --node-count 1 \
  --node-vm-size Standard_B2s \
  --generate-ssh-keys \
  --attach-acr "$ACR_NAME"
```

`--attach-acr` grants the AKS cluster's identity permission to pull images
from your registry — no extra credentials needed for that part.

## 3. Let GitHub Actions log in to Azure without a stored secret (OIDC)

```bash
# Create an app registration GitHub Actions will authenticate as.
APP_ID=$(az ad app create --display-name "gh-appointments-deploy" --query appId -o tsv)
az ad sp create --id "$APP_ID"

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)

# Give it just enough access: Contributor on the resource group we created.
az role assignment create \
  --assignee "$APP_ID" \
  --role "Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"

# AKS also requires this role to run kubectl commands against the cluster.
az role assignment create \
  --assignee "$APP_ID" \
  --role "Azure Kubernetes Service Cluster Admin Role" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.ContainerService/managedClusters/$AKS_CLUSTER_NAME"

# Federated credential: tells Azure to trust tokens GitHub issues for THIS
# repo's main branch, so no client secret ever needs to exist.
az ad app federated-credential create \
  --id "$APP_ID" \
  --parameters "{
    \"name\": \"gh-main-branch\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"repo:$GITHUB_ORG/$GITHUB_REPO:ref:refs/heads/main\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }"

echo "AZURE_CLIENT_ID=$APP_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
```

If you ever run the workflow manually via "Run workflow" (workflow_dispatch)
from a branch other than `main`, add another federated credential for that
ref the same way.

## 4. Add the values to your GitHub repo

In the GitHub repo → **Settings → Secrets and variables → Actions**:

**Secrets** tab:
| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | the `AZURE_CLIENT_ID` printed above |
| `AZURE_TENANT_ID` | the `AZURE_TENANT_ID` printed above |
| `AZURE_SUBSCRIPTION_ID` | the `AZURE_SUBSCRIPTION_ID` printed above |

**Variables** tab:
| Name | Value |
|---|---|
| `ACR_NAME` | your `$ACR_NAME` (no `.azurecr.io` suffix) |
| `AKS_CLUSTER_NAME` | your `$AKS_CLUSTER_NAME` |
| `AKS_RESOURCE_GROUP` | your `$RESOURCE_GROUP` |

## 5. Push and watch it deploy

```bash
git add .
git commit -m "Add Appointments microservice"
git push origin main
```

Go to the **Actions** tab on GitHub — "Build and deploy Appointments API"
should run automatically. The last step of the `deploy` job prints the
service's public IP (`kubectl get service appointments-api -n appointments`).
Give it a minute or two after the workflow finishes for Azure to assign the
external IP (`EXTERNAL-IP` starts as `<pending>`).

## Cost note

An AKS cluster + a running node is not free — a single `Standard_B2s` node
runs roughly $30-35/month, plus the ACR Basic tier (~$5/month). If this is
purely for learning/testing, remember to tear it down when you're done:

```bash
az group delete --name "$RESOURCE_GROUP" --yes --no-wait
```

That deletes everything created above (ACR, AKS, and their resources) in one
shot, since they all live in the same resource group.
