using Appointments.Api.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IAppointmentRepository, InMemoryAppointmentRepository>();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Allow the frontend (e.g. the salon's Next.js site) to call this API from
// a different origin. Tighten this to your real site's domain(s) before
// going to production.
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

// Swagger UI only in Development — don't expose the API's schema publicly
// once this is deployed.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

// Liveness/readiness probe for Kubernetes and load balancers.
app.MapHealthChecks("/healthz");

// Simple root so hitting the service root confirms it's up.
app.MapGet("/", () => Results.Ok(new { service = "Appointments.Api", status = "running" }));

app.Run();

// Needed so WebApplicationFactory<Program> works in integration tests later.
public partial class Program { }
