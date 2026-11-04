namespace Appointments.Api.Configuration;

/// <summary>
/// Límites por IP de los endpoints públicos (sección <c>RateLimiting</c>). Evitan que
/// alguien llene la agenda de reservas falsas o machaque la disponibilidad.
/// En Azure se cambian con variables de entorno, por ejemplo <c>RateLimiting__BookingsPerHour=20</c>.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Reservas (<c>POST /api/appointments</c>) por IP y por hora.</summary>
    public int BookingsPerHour { get; set; } = 10;

    /// <summary>Consultas de disponibilidad (<c>GET /api/availability</c>) por IP y por minuto.</summary>
    public int AvailabilityPerMinute { get; set; } = 60;
}
