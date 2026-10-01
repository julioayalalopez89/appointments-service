using Appointments.Api.Configuration;
using Appointments.Api.Data;
using Appointments.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Appointments.Api.Tests;

/// <summary>
/// Arranca la API completa en memoria (pipeline, filtros, rutas, JSON) para los
/// tests de integración. Sustituye solo lo externo:
/// <list type="bullet">
/// <item>SQL Server → base de datos en memoria de EF (una por instancia de la fábrica).</item>
/// <item>Reloj → <see cref="TestData.Now"/>.</item>
/// <item>Horario del negocio → <see cref="TestData.Business"/> (martes y sábado).</item>
/// <item>Clave de gestión → <see cref="AdminKey"/>.</item>
/// </list>
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminKey = "integration-test-key";

    private readonly string _databaseName = $"appointments-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Program exige una cadena de conexión; no se usa porque SQL Server se sustituye abajo.
        builder.UseSetting("ConnectionStrings:AppointmentsDb", "Server=unused-in-tests");

        builder.ConfigureTestServices(services =>
        {
            // Quitar el registro de SQL Server y usar la base de datos en memoria.
            services.RemoveAll<DbContextOptions<AppointmentsDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<AppointmentsDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now));

            services.PostConfigure<BusinessOptions>(options =>
            {
                var business = TestData.Business();
                options.TimeZone = business.TimeZone;
                options.SlotIntervalMinutes = business.SlotIntervalMinutes;
                options.MaxConcurrentAppointments = business.MaxConcurrentAppointments;
                options.OpeningHours = business.OpeningHours;
            });

            services.PostConfigure<SecurityOptions>(options => options.AdminApiKey = AdminKey);
        });
    }

    /// <summary>Cliente con la cabecera <c>X-Api-Key</c> correcta para los endpoints de gestión.</summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(RequireApiKeyAttribute.HeaderName, AdminKey);
        return client;
    }
}
