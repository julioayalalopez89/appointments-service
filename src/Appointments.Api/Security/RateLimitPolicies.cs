using System.Threading.RateLimiting;
using Appointments.Api.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Security;

/// <summary>
/// Rate limiting por IP con el limitador nativo de ASP.NET Core
/// (<c>Microsoft.AspNetCore.RateLimiting</c>). Los endpoints lo activan con
/// <c>[EnableRateLimiting(RateLimitPolicies.Bookings)]</c> o <c>[EnableRateLimiting(RateLimitPolicies.Availability)]</c>.
/// Al pasarse del límite se responde 429 con un mensaje y la cabecera <c>Retry-After</c>.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Política de <c>POST /api/appointments</c>: <see cref="RateLimitingOptions.BookingsPerHour"/> por hora.</summary>
    public const string Bookings = "bookings";

    /// <summary>Política de <c>GET /api/availability</c>: <see cref="RateLimitingOptions.AvailabilityPerMinute"/> por minuto.</summary>
    public const string Availability = "availability";

    public static IServiceCollection AddAppointmentsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            // Ventana fija por IP. Las opciones se leen en cada partición nueva (no al
            // arrancar), así los tests pueden cambiarlas con PostConfigure.
            limiter.AddPolicy(Bookings, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => Window(GetOptions(context).BookingsPerHour, TimeSpan.FromHours(1))));

            limiter.AddPolicy(Availability, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => Window(GetOptions(context).AvailabilityPerMinute, TimeSpan.FromMinutes(1))));

            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var response = rejected.HttpContext.Response;
                string message;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                    response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    message = $"Too many requests. Please try again in {FormatWait(seconds)}.";
                }
                else
                {
                    message = "Too many requests. Please try again later.";
                }

                await response.WriteAsJsonAsync(new { message }, cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Clave de la partición: la IP del cliente. Detrás del ingress de Azure Container Apps
    /// la IP real llega en <c>X-Forwarded-For</c>; <c>UseForwardedHeaders</c> (Program.cs)
    /// la copia a <c>RemoteIpAddress</c> antes de llegar aquí.
    /// </summary>
    internal static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitingOptions GetOptions(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static FixedWindowRateLimiterOptions Window(int permitLimit, TimeSpan window) => new()
    {
        PermitLimit = Math.Max(1, permitLimit),
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string FormatWait(int seconds) =>
        seconds >= 120 ? $"{(int)Math.Ceiling(seconds / 60.0)} minutes"
        : seconds == 1 ? "1 second"
        : $"{seconds} seconds";
}
