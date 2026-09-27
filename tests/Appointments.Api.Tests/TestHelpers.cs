using Appointments.Api.Configuration;
using Appointments.Api.Controllers;
using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Tests;

/// <summary>Reloj fijo para que los tests no dependan de la fecha real.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
}

internal static class TestData
{
    /// <summary>"Ahora" en los tests: lunes 28/09/2026 12:00 hora de Nueva York (EDT, UTC-4).</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-4));

    /// <summary>Martes 29/09/2026 (abierto 9:00-19:00) a la hora local indicada, en EDT.</summary>
    public static DateTimeOffset Tuesday(int hour, int minute = 0) =>
        new(2026, 9, 29, hour, minute, 0, TimeSpan.FromHours(-4));

    public static BusinessOptions Business(int maxConcurrent = 1) => new()
    {
        TimeZone = "America/New_York",
        SlotIntervalMinutes = 15,
        MaxConcurrentAppointments = maxConcurrent,
        OpeningHours =
        {
            new DaySchedule { Day = DayOfWeek.Tuesday, Open = new TimeOnly(9, 0), Close = new TimeOnly(19, 0) },
            new DaySchedule { Day = DayOfWeek.Saturday, Open = new TimeOnly(9, 0), Close = new TimeOnly(17, 0) },
        },
    };

    public static AppointmentsController Controller(IAppointmentRepository repository, int maxConcurrent = 1) =>
        new(repository,
            NullLogger<AppointmentsController>.Instance,
            Options.Create(Business(maxConcurrent)),
            new FixedTimeProvider(Now));

    public static CreateAppointmentRequest Request(DateTimeOffset start, int durationMinutes = 30, string? provider = null) => new()
    {
        CustomerName = "Test Customer",
        CustomerPhone = "+1 305 555 0100",
        ServiceName = "Haircut",
        ProviderName = provider,
        StartTime = start,
        DurationMinutes = durationMinutes,
    };
}
