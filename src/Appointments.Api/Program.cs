using Appointments.Api.Data;
using Appointments.Api.Configuration;
using Appointments.Api.Repositories;
using Appointments.Api.Security;
using Appointments.Api.Services.Notifications;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Horario del negocio, zona horaria y capacidad. Se valida al arrancar:
// una configuración inválida detiene la app con un mensaje claro.
builder.Services.AddSingleton<IValidateOptions<BusinessOptions>, BusinessOptionsValidator>();
builder.Services.AddOptions<BusinessOptions>()
    .Bind(builder.Configuration.GetSection(BusinessOptions.SectionName))
    .ValidateOnStart();

// Clave de los endpoints de gestión (cabecera X-Api-Key). Ver Security/RequireApiKeyAttribute.cs.
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

// Avisos por email al salón (Resend) al crear o cancelar una cita. Se envían en segundo
// plano; sin API key o sin email del salón no se envía nada (solo un aviso en el log).
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<INotificationService, EmailNotificationService>();
builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHostedService<EmailNotificationWorker>();

// Límite de peticiones por IP en los endpoints públicos (reservar y disponibilidad).
// Ver Security/RateLimitPolicies.cs y la sección RateLimiting de appsettings.json.
builder.Services.AddAppointmentsRateLimiting(builder.Configuration);

// La API corre detrás del ingress de Azure Container Apps: la IP real del cliente
// llega en X-Forwarded-For. ForwardLimit = 1 toma solo la última entrada (la que
// añade el ingress), así un cliente no puede saltarse el límite inventando IPs.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // El ingress no tiene una IP fija conocida: se confía en el proxy inmediato.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Reloj inyectable: en los tests se sustituye por una hora fija.
builder.Services.AddSingleton(TimeProvider.System);

var connectionString = builder.Configuration.GetConnectionString("AppointmentsDb")
    ?? throw new InvalidOperationException(
        "Connection string 'AppointmentsDb' not found. Set it via the ConnectionStrings__AppointmentsDb environment variable.");

builder.Services.AddDbContext<AppointmentsDbContext>(options =>
    options.UseSqlServer(connectionString));

// Scoped (not Singleton) porque depende del DbContext, que también es Scoped.
builder.Services.AddScoped<IAppointmentRepository, EfAppointmentRepository>();

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Incluye los comentarios /// de los controladores en la documentación de Swagger.
    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlFile))
    {
        options.IncludeXmlComments(xmlFile);
    }
});

// Dominios del frontend que pueden llamar a la API desde el navegador.
// Producción: 305hairstyle.com (con y sin www). Desarrollo: también Vite (localhost:5173).
var allowedOrigins = CorsOrigins.Resolve(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseForwardedHeaders();

// Aplica las migraciones automáticamente al arrancar — crea las tablas la
// primera vez, sin que tengas que correr un comando aparte en producción.
// Solo con una base de datos relacional: los tests de integración usan el
// proveedor en memoria de EF, que no tiene migraciones.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppointmentsDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
// Después de CORS: así la respuesta 429 lleva las cabeceras CORS y la web puede leer el mensaje.
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Ok(new { service = "Appointments.Api", status = "running", version = "1.1" }));

app.Run();

public partial class Program { }