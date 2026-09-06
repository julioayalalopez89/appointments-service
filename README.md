# Appointments Service

A small ASP.NET Core Web API microservice for managing appointments —
built with a hair salon in mind, but generic enough (free-text service/
provider names, no salon-specific fields) to work for any appointment-based
business: barbershop, spa, tutoring, repair shop, etc.

This project follows the pattern from the Microsoft Learn module
[Deploy a cloud-native .NET microservice automatically with GitHub Actions and Azure Pipelines](https://learn.microsoft.com/en-us/training/modules/microservices-devops-aspnet-core/):
containerize the service, then use GitHub Actions to build the image, push it
to Azure Container Registry, and deploy it to Azure Kubernetes Service.

## What's here

```
src/Appointments.Api/         The Web API (controllers, models, in-memory repository)
  Dockerfile                  Multi-stage build → small runtime image
k8s/                          Kubernetes manifests (namespace, deployment, service)
.github/workflows/
  ci.yml                      Builds the project on every pull request (no Azure needed)
  build-and-deploy.yml        Builds & pushes the image to ACR, then deploys to AKS on push to main
docs/AZURE_SETUP.md           One-time checklist: Azure resources + GitHub secrets
```

## Run it locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd src/Appointments.Api
dotnet run
```

The API listens on the port printed in the console (e.g. `http://localhost:5080`).

| Method | Route | Description |
|---|---|---|
| GET | `/api/appointments` | List appointments (optional `?date=2026-09-10&status=Booked`) |
| GET | `/api/appointments/{id}` | Get one appointment |
| POST | `/api/appointments` | Book a new appointment |
| PUT | `/api/appointments/{id}` | Reschedule / edit an appointment |
| POST | `/api/appointments/{id}/cancel` | Cancel (soft delete, keeps history) |
| DELETE | `/api/appointments/{id}` | Permanently delete |
| GET | `/healthz` | Health check (used by Kubernetes probes) |

Example booking request:

```bash
curl -X POST http://localhost:5080/api/appointments \
  -H "Content-Type: application/json" \
  -d '{
        "customerName": "Maria Gomez",
        "customerPhone": "+13055551234",
        "serviceName": "Haircut + Blowout",
        "providerName": "Jasmine",
        "startTime": "2026-09-10T15:00:00-04:00",
        "durationMinutes": 45
      }'
```

Booking a second appointment for the same `providerName` that overlaps in
time returns `409 Conflict` — the double-booking check is already built in.

Swagger UI (interactive API docs) is at `/swagger` when running in
Development — `dotnet run` opens it in your browser automatically.

## Run it in Docker

```bash
cd src/Appointments.Api
docker build -t appointments-api .
docker run -p 8080:8080 appointments-api
curl http://localhost:8080/healthz
```

(I wrote and reviewed this Dockerfile against the standard ASP.NET Core
multi-stage pattern, but couldn't actually execute a `docker build` from this
sandboxed session — it has no Docker daemon and no route to
`mcr.microsoft.com`. It will build normally on your machine or in GitHub
Actions, both of which have real Docker + internet access. Worth running the
two commands above once locally just to confirm before your first push.)

## Ship it to Azure

1. Follow **[docs/AZURE_SETUP.md](docs/AZURE_SETUP.md)** once — creates the
   resource group, Container Registry, AKS cluster, and a passwordless
   (OIDC) connection from GitHub Actions to your Azure subscription.
2. Push to `main`. The **Build and deploy Appointments API** workflow builds
   the container image with `az acr build`, pushes it to your registry, then
   applies the Kubernetes manifests in `k8s/` to your AKS cluster.
3. Watch it run under the repo's **Actions** tab on GitHub.

Every pull request also runs `ci.yml`, a plain `dotnet build` — that one
needs no Azure access, so it's safe to have running from day one even before
you've done the Azure setup.

## Design notes / why it's built this way

- **Only one NuGet package** (`Swashbuckle.AspNetCore`, for the Swagger UI).
  Everything else (controllers, DI, health checks, CORS) ships in the
  ASP.NET Core shared framework. `dotnet restore`/`run` need internet access
  the first time to pull that one package — after that it's cached locally.
- **In-memory storage.** `InMemoryAppointmentRepository` is a placeholder —
  data resets on every restart, and doesn't work correctly if you scale past
  1 replica (each pod gets its own copy). The whole point of hiding storage
  behind `IAppointmentRepository` is that swapping in a real database later
  is a one-file change, not a rewrite. `k8s/deployment.yaml` is pinned to
  `replicas: 1` with a comment explaining why — bump it only after the
  storage is real.
- **CORS** is wired up (see `Program.cs`) so the 305 Hair Style Next.js site
  (or any frontend) can call this API from the browser. Update
  `Cors:AllowedOrigins` in `appsettings.json` (or the `k8s/deployment.yaml`
  env var) to your real frontend domain(s) before going live — it currently
  defaults to `http://localhost:3000` and the k8s manifest sets it to
  `https://305hairstyle.com` as an example.
- **Soft-delete cancel vs. hard delete.** `POST /cancel` marks an appointment
  cancelled but keeps the record (useful for a future "appointment history"
  view); `DELETE` removes it outright. Point your UI at `/cancel` for normal
  use.

## Next steps worth considering

- Swap `InMemoryAppointmentRepository` for a real database (Azure SQL,
  Postgres, or even SQLite to start).
- Add authentication (an API key header is the simplest starting point) —
  right now every endpoint is open to anyone who can reach the URL.
- If this needs to serve more than one business/tenant later, that's the
  point to promote `ServiceName`/`ProviderName` from free text into real
  `Service` and `Provider` entities, and add a `BusinessId` — but there's no
  reason to build that until you actually need it.
