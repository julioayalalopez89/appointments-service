using Appointments.Api.Data;
using Appointments.Api.Configuration;
using Appointments.Api.Repositories;
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
builder.Services.AddSwaggerGen();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Aplica las migraciones automáticamente al arrancar — crea las tablas la
// primera vez, sin que tengas que correr un comando aparte en producción.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppointmentsDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Ok(new { service = "Appointments.Api", status = "running", version = "1.1" }));

app.Run();

public partial class Program { }