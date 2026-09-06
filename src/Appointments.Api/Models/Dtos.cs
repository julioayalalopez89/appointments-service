using System.ComponentModel.DataAnnotations;

namespace Appointments.Api.Models;

/// <summary>Payload to book a new appointment.</summary>
public class CreateAppointmentRequest
{
    [Required, MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    [Required, Phone, MaxLength(30)]
    public string CustomerPhone { get; set; } = string.Empty;

    [EmailAddress, MaxLength(200)]
    public string? CustomerEmail { get; set; }

    [Required, MaxLength(200)]
    public string ServiceName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ProviderName { get; set; }

    [Required]
    public DateTimeOffset StartTime { get; set; }

    [Range(5, 480)]
    public int DurationMinutes { get; set; } = 30;

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>Payload to reschedule/edit an existing appointment.</summary>
public class UpdateAppointmentRequest
{
    [Required, MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    [Required, Phone, MaxLength(30)]
    public string CustomerPhone { get; set; } = string.Empty;

    [EmailAddress, MaxLength(200)]
    public string? CustomerEmail { get; set; }

    [Required, MaxLength(200)]
    public string ServiceName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ProviderName { get; set; }

    [Required]
    public DateTimeOffset StartTime { get; set; }

    [Range(5, 480)]
    public int DurationMinutes { get; set; } = 30;

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public class CancelAppointmentRequest
{
    [MaxLength(500)]
    public string? Reason { get; set; }
}
