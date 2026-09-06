namespace Appointments.Api.Models;

/// <summary>
/// Lifecycle of a booking. Kept small on purpose — add states (e.g. NoShow)
/// only when a real workflow needs them.
/// </summary>
public enum AppointmentStatus
{
    Booked,
    Cancelled,
    Completed
}

/// <summary>
/// A single appointment slot for a service-based business (hair salon,
/// barbershop, spa, tutoring, repair shop, etc.). Deliberately generic:
/// "ServiceName" and "ProviderName" are free text rather than foreign keys
/// into Service/Stylist tables, so this works for any business without a
/// schema change. Promote them to real entities later if you need to manage
/// a fixed service/staff list (pricing, durations, availability rules).
/// </summary>
public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }

    public string ServiceName { get; set; } = string.Empty;
    public string? ProviderName { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public DateTimeOffset EndTime => StartTime.AddMinutes(DurationMinutes);

    public string? Notes { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
