# Appointments Service

A small ASP.NET Core Web API microservice for managing appointments —
built with a hair salon in mind, but generic enough (free-text service/
provider names, no salon-specific fields) to work for any appointment-based
business: barbershop, spa, tutoring, repair shop, etc.

It is the booking backend of [305hairstyle.com](https://305hairstyle.com)
(`305hairstyle_web`).

## Architecture (current)

```
305hairstyle.com (React)  ──HTTPS + CORS──▶  Azure Container Apps: appointments-api
                                               │  ASP.NET Core 8 Web API (this repo)
                                               ▼
                                             Azure SQL (EF Core, migrations applied on startup)

GitHub Actions: PR → ci.yml (build + tests)
                push to main → build-and-deploy.yml (az acr build → ACR → az containerapp update → /healthz smoke test)
```

- **Hosting:** Azure Container Apps (app `appointments-api`, environment `cae-appointments`).
  The image is built with ACR Tasks and pushed to Azure Container Registry.
- **Storage:** Azure SQL through EF Core (`EfAppointmentRepository`). The connection string comes
  from `ConnectionStrings__AppointmentsDb`; migrations run automatically at startup.
- **Security:** public booking and availability endpoints; management endpoints need `X-Api-Key`
  (see below). Secrets live in Container Apps, never in the repo.
- The project started from the Microsoft Learn module
  [Deploy a cloud-native .NET microservice automatically with GitHub Actions and Azure Pipelines](https://learn.microsoft.com/en-us/training/modules/microservices-devops-aspnet-core/),
  which deploys to AKS. It moved to Container Apps; the old Kubernetes manifests are kept in
  `docs/legacy-aks/` for reference only.

## What's here

```
src/Appointments.Api/         The Web API (controllers, models, EF Core data access, booking rules)
  Dockerfile                  Multi-stage build → small runtime image
  Migrations/                 EF Core migrations (applied on startup)
tests/Appointments.Api.Tests/ xUnit tests (no database needed)
.github/workflows/
  ci.yml                      Builds and runs the tests on every pull request (no Azure needed)
  build-and-deploy.yml        Builds & pushes the image to ACR, then updates the Container App on push to main
docs/AZURE_SETUP.md           One-time Azure + GitHub OIDC setup (written for the original AKS setup)
docs/legacy-aks/              Old Kubernetes manifests (not used since the move to Container Apps)
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
| GET | `/healthz` | Health check (used by the post-deploy smoke test) |

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

Run the tests with `dotnet test` (no database needed). Besides the unit tests, `tests/Appointments.Api.Tests/ApiIntegrationTests.cs` starts the whole API in memory with `WebApplicationFactory` and the EF Core in-memory provider, and calls every endpoint over HTTP. CI runs them on every pull request.

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

## Emails: avisos al salón y confirmación a la clienta (Resend)

Cada vez que se **reserva** o se **cancela** una cita, la API envía un email al salón con la fecha y la
hora local del salón, el servicio, la duración, el estilista (si lo hay), el nombre, el teléfono (enlace
`tel:` y de WhatsApp), el email, las notas y un enlace a la agenda (`https://305hairstyle.com/admin/`).

Además, al **reservar**, si la clienta dejó email, recibe una **confirmación** con la fecha y la hora
(hora del salón), el servicio, el estilista (si lo hay), la dirección del salón (con enlace a Maps) y un
enlace de WhatsApp con un mensaje ya escrito para cambiar o cancelar. Si responde al email, la respuesta
llega al email del salón (`reply_to`). No incluye las notas ni el enlace a la agenda.

- El envío ocurre **en segundo plano**: no retrasa la respuesta de la API.
- Si falta la API key o el remitente → no se envía nada y queda un aviso en el log.
  Si solo falta el email del salón → no hay avisos al salón, pero sí confirmaciones a las clientas.
- Si Resend falla → la cita se guarda igual y el error queda en el log.
- Se usa la API HTTP de Resend directamente (`POST https://api.resend.com/emails`), sin SDK.

Configuración (sección `Notifications`; nada de esto va en el repo salvo el remitente por defecto):

| Variable de entorno | Qué es |
|---|---|
| `Notifications__ResendApiKey` | API key de Resend (`re_...`). **Secret.** |
| `Notifications__SalonEmail` | Email que recibe los avisos. |
| `Notifications__FromEmail` | Remitente. Por defecto `305 Hair Style <reservas@305hairstyle.com>`. |
| `Notifications__AdminUrl` | Enlace a la agenda. Por defecto `https://305hairstyle.com/admin/`. |
| `Notifications__SalonName` | Nombre en el asunto y la firma de la confirmación. Por defecto `305 Hair Style`. |
| `Notifications__SalonAddress` | Dirección en la confirmación. Por defecto `8631 Coral Wy, Miami, FL 33155`. |
| `Notifications__SalonMapsUrl` | Enlace a Google Maps de la dirección (opcional). |
| `Notifications__SalonWhatsApp` | WhatsApp del salón para cambios o cancelaciones. Por defecto `+1 786 566 9938`. Sin número, la confirmación dice "responde a este email". |

### Pasos para Julio (una sola vez)

1. Crear una cuenta en [resend.com](https://resend.com) y una API key con permiso de envío (*Sending access*).
2. En Resend → **Domains** → *Add domain* → `305hairstyle.com`. Copiar los registros DNS que muestra
   (TXT/MX de SPF y el TXT de DKIM) en el DNS de **Hostinger** y pulsar *Verify* en Resend.
   Mientras el dominio no esté verificado se puede probar con `Notifications__FromEmail=onboarding@resend.dev`,
   pero Resend solo deja enviar así al email de la propia cuenta.
3. Guardar la key como secret del Container App y exponer la configuración:

```bash
az containerapp secret set --name appointments-api --resource-group <tu-resource-group> \
  --secrets resend-api-key=<re_tu_api_key>
az containerapp update --name appointments-api --resource-group <tu-resource-group> \
  --set-env-vars Notifications__ResendApiKey=secretref:resend-api-key \
                 Notifications__SalonEmail=<email-del-salon>
```

4. Hacer una reserva de prueba en la web y comprobar que llega el email (y, si no, revisar los logs
   del Container App: `az containerapp logs show --name appointments-api --resource-group <tu-resource-group>`).

En local se puede probar con user-secrets:

```bash
cd src/Appointments.Api
dotnet user-secrets set "Notifications:ResendApiKey" "<re_tu_api_key>"
dotnet user-secrets set "Notifications:SalonEmail" "<tu-email>"
dotnet user-secrets set "Notifications:FromEmail" "onboarding@resend.dev"
```

## Configuración del negocio (horario y capacidad)

La sección `Business` de `appsettings.json` define las reglas para calcular
huecos libres. Se valida al arrancar: si algo está mal, la app no arranca y
el log dice qué corregir.

| Clave | Por defecto | Qué es |
|---|---|---|
| `TimeZone` | `America/New_York` | Zona horaria IANA del negocio |
| `SlotIntervalMinutes` | `15` | Cada cuántos minutos empieza un hueco reservable |
| `MaxConcurrentAppointments` | `1` | Citas simultáneas sin estilista asignado (sillas/estilistas) |
| `OpeningHours` | Sáb, Dom y Lun 9:00–19:00 | Horario por día; un día que no aparece está cerrado |

> Horario real del salón confirmado por Julio (el mismo que muestra 305hairstyle.com).
> Si en Azure hay variables `Business__OpeningHours__*`, **mandan sobre `appsettings.json`**: bórralas o cámbialas para que coincidan.

En Azure Container Apps se sobrescribe con variables de entorno, por ejemplo:

```
Business__TimeZone=America/New_York
Business__MaxConcurrentAppointments=2
Business__OpeningHours__0__Day=Saturday
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

## Rate limiting (anti-spam)

Los endpoints públicos tienen un límite de peticiones **por IP** (limitador nativo de
ASP.NET Core, ventana fija). Los de gestión (con `X-Api-Key`) no tienen límite.

| Endpoint | Clave | Por defecto |
|---|---|---|
| `POST /api/appointments` | `RateLimiting:BookingsPerHour` | 10 reservas por hora |
| `GET /api/availability` | `RateLimiting:AvailabilityPerMinute` | 60 consultas por minuto |

Al pasarse del límite la API responde **429 Too Many Requests** con la cabecera
`Retry-After` (segundos) y un JSON como
`{ "message": "Too many requests. Please try again in 42 minutes." }`.

La IP del cliente se toma de `X-Forwarded-For` (la añade el ingress de Azure Container
Apps; solo se usa la última entrada). Los contadores viven en memoria: si la app escala a
varias réplicas, cada una cuenta por separado.

Para cambiar los límites en Azure:

```bash
az containerapp update --name appointments-api --resource-group <rg> \
  --set-env-vars RateLimiting__BookingsPerHour=20 RateLimiting__AvailabilityPerMinute=120
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

The Azure resources already exist (resource group, ACR, Container Apps
environment `cae-appointments`, app `appointments-api`, Azure SQL) and GitHub
Actions logs in with OIDC (no stored passwords).

1. Merge a pull request into `main`.
2. The **Build and deploy Appointments API** workflow builds the image with
   `az acr build`, pushes it to the registry, runs `az containerapp update`
   with the new image and then smoke-tests `/healthz`.
3. Watch it run under the repo's **Actions** tab on GitHub.

The deploy only changes the image: environment variables and secrets set on
the Container App (`ConnectionStrings__AppointmentsDb`, `Security__AdminApiKey`,
`Business__*`, `Cors__*`) are kept between deploys.

Every pull request also runs `ci.yml` (build + tests). It needs no Azure
access.

> `docs/AZURE_SETUP.md` still describes the original AKS provisioning. The OIDC
> and GitHub secrets/variables parts still apply; the AKS cluster steps do not.

## Design notes / why it's built this way

- **Few NuGet packages**: `Swashbuckle.AspNetCore` (Swagger UI) and EF Core
  for SQL Server. Everything else (controllers, DI, health checks, CORS) ships
  in the ASP.NET Core shared framework.
- **Storage behind an interface.** `IAppointmentRepository` has the real
  `EfAppointmentRepository` (Azure SQL) and an `InMemoryAppointmentRepository`
  that the unit tests use.
- **CORS** is wired up (see `Program.cs` and `Configuration/CorsOrigins.cs`)
  so the 305 Hair Style site can call this API from the browser. See
  [CORS (dominios del frontend)](#cors-dominios-del-frontend) below.
- **Soft-delete cancel vs. hard delete.** `POST /cancel` marks an appointment
  cancelled but keeps the record (useful for a future "appointment history"
  view); `DELETE` removes it outright. Point your UI at `/cancel` for normal
  use.

## Next steps worth considering

- If this needs to serve more than one business/tenant later, that's the
  point to promote `ServiceName`/`ProviderName` from free text into real
  `Service` and `Provider` entities, and add a `BusinessId` — but there's no
  reason to build that until you actually need it.
