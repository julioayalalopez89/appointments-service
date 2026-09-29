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

Booking rules (see `Services/BookingRules.cs`):

- **With `providerName`**: an overlapping appointment for the same stylist returns `409 Conflict`.
- **Without `providerName`** (the website doesn't pick a stylist): `409 Conflict` only when the
  salon already has `Business:MaxConcurrentAppointments` active appointments at the same time.
- Appointments in the past, on a closed day or outside `Business:OpeningHours` return `400 Bad Request`.

Run the unit tests with `dotnet test`.

### Availability (public)

`GET /api/availability?date=2026-10-03&durationMinutes=60` returns the free start
times for an appointment **without a stylist**, in the business time zone:

```json
{ "date": "2026-10-03", "timeZone": "America/New_York", "slots": ["09:00", "09:15", "10:30"] }
```

- Uses `Business:OpeningHours`, `SlotIntervalMinutes` and `MaxConcurrentAppointments`.
- Excludes past times (when `date` is today) and slots that would end after closing.
- Closed day → `"slots": []`. Missing `date` or `durationMinutes` outside 5–480 → `400`.
- `durationMinutes` is optional (default `30`). The endpoint needs no credentials.

Swagger UI (interactive API docs) is at `/swagger` when running in
Development — `dotnet run` opens it in your browser automatically.

## Seguridad: endpoints de gestión (API key)

Públicos (sin credenciales): `POST /api/appointments`, `GET /api/availability`, `/healthz` y `/`.

Protegidos con la cabecera `X-Api-Key`: `GET /api/appointments`, `GET /api/appointments/{id}`,
`PUT /api/appointments/{id}`, `POST /api/appointments/{id}/cancel` y `DELETE /api/appointments/{id}`.

- Sin cabecera o con una clave incorrecta → `401`.
- Si el servidor **no tiene clave configurada** → `503` (los endpoints de gestión quedan cerrados, nunca abiertos).

La clave se lee de `Security:AdminApiKey` y **nunca se guarda en el repo**.

En local (user-secrets):

```bash
cd src/Appointments.Api
dotnet user-secrets init
dotnet user-secrets set "Security:AdminApiKey" "$(openssl rand -hex 32)"
curl -H "X-Api-Key: <tu-clave>" http://localhost:<puerto>/api/appointments
```

En Azure Container Apps (se guarda como secret y se expone como variable de entorno):

```bash
az containerapp secret set --name appointments-api --resource-group <tu-resource-group> \
  --secrets admin-api-key=<tu-clave>
az containerapp update --name appointments-api --resource-group <tu-resource-group> \
  --set-env-vars Security__AdminApiKey=secretref:admin-api-key
```

El workflow de deploy solo cambia la imagen, así que la variable se mantiene entre despliegues.

## Configuración del negocio (horario y capacidad)

La sección `Business` de `appsettings.json` define las reglas para calcular
huecos libres. Se valida al arrancar: si algo está mal, la app no arranca y
el log dice qué corregir.

| Clave | Por defecto | Qué es |
|---|---|---|
| `TimeZone` | `America/New_York` | Zona horaria IANA del negocio |
| `SlotIntervalMinutes` | `15` | Cada cuántos minutos empieza un hueco reservable |
| `MaxConcurrentAppointments` | `1` | Citas simultáneas sin estilista asignado (sillas/estilistas) |
| `OpeningHours` | Mar–Vie 9:00–19:00, Sáb 9:00–17:00 | Horario por día; un día que no aparece está cerrado |

> ⚠️ El horario incluido es **de ejemplo** hasta confirmar el horario real del salón.

En Azure Container Apps se sobrescribe con variables de entorno, por ejemplo:

```
Business__TimeZone=America/New_York
Business__MaxConcurrentAppointments=2
Business__OpeningHours__0__Day=Tuesday
Business__OpeningHours__0__Open=09:00
Business__OpeningHours__0__Close=19:00
```

## CORS (dominios del frontend)

Solo estos orígenes pueden llamar a la API desde el navegador:

| Entorno | Orígenes permitidos |
|---|---|
| Producción | `https://305hairstyle.com`, `https://www.305hairstyle.com` (de `Cors:AllowedOrigins` en `appsettings.json`) |
| Desarrollo (`ASPNETCORE_ENVIRONMENT=Development`) | Los anteriores + `http://localhost:5173` (Vite, `305hairstyle_web`) |

Si `Cors:AllowedOrigins` está vacío se usan los dos dominios del salón. Las
entradas se limpian al arrancar: espacios, entradas vacías, duplicados y la
`/` final (`https://305hairstyle.com/` no coincidiría nunca con el `Origin`
que manda el navegador).

En Azure Container Apps se sobrescribe con variables de entorno (una por
índice). Ojo: sobrescribir solo `__0` deja el `__1` de `appsettings.json`,
así que define siempre la lista completa:

```bash
az containerapp update --name appointments-api --resource-group <rg> \
  --set-env-vars \
    Cors__AllowedOrigins__0=https://305hairstyle.com \
    Cors__AllowedOrigins__1=https://www.305hairstyle.com
```

Con los valores por defecto no hace falta definir nada en Azure; solo si
cambian los dominios (por ejemplo, un dominio de staging).

Para probar en local, con la API en marcha en modo Development (`dotnet run`, perfil `http`):

```bash
curl -i -X OPTIONS http://localhost:5043/api/availability \
  -H "Origin: http://localhost:5173" \
  -H "Access-Control-Request-Method: GET"
# → 204 con Access-Control-Allow-Origin: http://localhost:5173
```

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
- **CORS** is wired up (see `Program.cs` and `Configuration/CorsOrigins.cs`)
  so the 305 Hair Style site can call this API from the browser. See
  [CORS (dominios del frontend)](#cors-dominios-del-frontend) below.
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
