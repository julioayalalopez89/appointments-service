using Appointments.Api.Configuration;
using Appointments.Api.Models;

namespace Appointments.Api.Services;

/// <summary>
/// Reglas de negocio para aceptar una cita: que esté en el futuro, dentro del
/// horario de apertura y que no supere la capacidad del salón. Es lógica pura
/// (sin base de datos ni HTTP) para poder probarla con tests unitarios.
/// </summary>
public static class BookingRules
{
    /// <summary>
    /// Devuelve un mensaje de error si la cita no se puede reservar a esa hora
    /// (en el pasado o fuera del horario de apertura), o null si es válida.
    /// </summary>
    public static string? ValidateSchedule(BusinessOptions options, DateTimeOffset start, int durationMinutes, DateTimeOffset now)
    {
        if (start < now)
        {
            return "The appointment start time is in the past.";
        }

        var timeZone = options.GetTimeZoneInfo();
        var localStart = TimeZoneInfo.ConvertTime(start, timeZone);
        var localEnd = TimeZoneInfo.ConvertTime(start.AddMinutes(durationMinutes), timeZone);

        var schedule = options.GetScheduleFor(localStart.DayOfWeek);
        if (schedule is null)
        {
            return $"The business is closed on {localStart.DayOfWeek}.";
        }

        var startTime = TimeOnly.FromDateTime(localStart.DateTime);
        var endTime = TimeOnly.FromDateTime(localEnd.DateTime);
        var endsSameDay = localEnd.Date == localStart.Date;

        if (startTime < schedule.Open || !endsSameDay || endTime > schedule.Close)
        {
            return $"The appointment must be within opening hours ({schedule.Day} {schedule.Open:HH\\:mm}-{schedule.Close:HH\\:mm}, {options.TimeZone}).";
        }

        return null;
    }

    /// <summary>
    /// Decide si una cita nueva choca con las existentes.
    /// - Con estilista: choca si ese estilista ya tiene una cita solapada.
    /// - Sin estilista: choca si en algún momento del intervalo ya hay
    ///   <see cref="BusinessOptions.MaxConcurrentAppointments"/> citas activas a la vez.
    /// </summary>
    /// <param name="overlapping">Citas activas (no canceladas) que se solapan con [start, end).</param>
    public static bool HasConflict(BusinessOptions options, string? providerName, DateTimeOffset start, DateTimeOffset end, IEnumerable<Appointment> overlapping)
    {
        var active = overlapping
            .Where(a => a.Status != AppointmentStatus.Cancelled && a.StartTime < end && start < a.EndTime)
            .ToList();

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return active.Any(a => string.Equals(a.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        }

        return PeakConcurrency(active, start, end) >= options.MaxConcurrentAppointments;
    }

    /// <summary>
    /// Horas de inicio libres para una cita sin estilista de <paramref name="durationMinutes"/>
    /// minutos el día <paramref name="date"/> (hora local del negocio). Usa el horario de
    /// apertura, el intervalo de slots y la capacidad; excluye horas pasadas y slots
    /// que no terminan antes del cierre. Día cerrado → lista vacía.
    /// </summary>
    /// <param name="options">Configuración del negocio.</param>
    /// <param name="date">Día a consultar, en la zona horaria del negocio.</param>
    /// <param name="durationMinutes">Duración de la cita en minutos.</param>
    /// <param name="now">Hora actual (para excluir slots pasados).</param>
    /// <param name="appointments">Citas de ese día (las canceladas se ignoran).</param>
    public static IReadOnlyList<TimeOnly> GetAvailableSlots(BusinessOptions options, DateOnly date, int durationMinutes, DateTimeOffset now, IEnumerable<Appointment> appointments)
    {
        var schedule = options.GetScheduleFor(date.DayOfWeek);
        if (schedule is null || durationMinutes <= 0)
        {
            return Array.Empty<TimeOnly>();
        }

        var timeZone = options.GetTimeZoneInfo();
        var active = appointments.Where(a => a.Status != AppointmentStatus.Cancelled).ToList();
        var openMinutes = (int)schedule.Open.ToTimeSpan().TotalMinutes;
        var closeMinutes = (int)schedule.Close.ToTimeSpan().TotalMinutes;
        var interval = Math.Max(1, options.SlotIntervalMinutes);

        var slots = new List<TimeOnly>();
        for (var minute = openMinutes; minute + durationMinutes <= closeMinutes; minute += interval)
        {
            var slot = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute));
            var start = ToBusinessTime(timeZone, date.ToDateTime(slot));
            if (start < now)
            {
                continue;
            }

            if (!HasConflict(options, null, start, start.AddMinutes(durationMinutes), active))
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    /// <summary>Inicio y fin (exclusivo) de un día en la zona horaria del negocio.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) GetDayBounds(BusinessOptions options, DateOnly date)
    {
        var timeZone = options.GetTimeZoneInfo();
        return (
            ToBusinessTime(timeZone, date.ToDateTime(TimeOnly.MinValue)),
            ToBusinessTime(timeZone, date.AddDays(1).ToDateTime(TimeOnly.MinValue)));
    }

    /// <summary>Convierte una fecha/hora local del negocio en un DateTimeOffset con su offset (EST/EDT).</summary>
    private static DateTimeOffset ToBusinessTime(TimeZoneInfo timeZone, DateTime local) =>
        new(local, timeZone.GetUtcOffset(local));

    /// <summary>
    /// Máximo de citas simultáneas dentro de [start, end). No basta con contar
    /// las solapadas: dos citas seguidas (10:00-10:30 y 10:30-11:00) solapan con
    /// una de 10:00-11:00 pero nunca ocupan dos sillas a la vez.
    /// </summary>
    private static int PeakConcurrency(IReadOnlyList<Appointment> appointments, DateTimeOffset start, DateTimeOffset end)
    {
        // Barrido por eventos: +1 al empezar, -1 al terminar. Con la misma hora,
        // los finales van primero para que citas seguidas no cuenten como simultáneas.
        var events = appointments
            .SelectMany(a => new[]
            {
                (Time: a.StartTime < start ? start : a.StartTime, Delta: +1),
                (Time: a.EndTime > end ? end : a.EndTime, Delta: -1),
            })
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Delta);

        var current = 0;
        var peak = 0;
        foreach (var e in events)
        {
            current += e.Delta;
            peak = Math.Max(peak, current);
        }

        return peak;
    }
}
