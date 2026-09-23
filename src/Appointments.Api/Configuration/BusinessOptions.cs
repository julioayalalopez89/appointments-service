namespace Appointments.Api.Configuration;

/// <summary>
/// Reglas del negocio para calcular disponibilidad: zona horaria, horario de
/// apertura por día, tamaño de los huecos y cuántas citas caben a la vez.
/// Se lee de la sección "Business" de la configuración, así que en Azure se
/// puede sobrescribir con variables de entorno (Business__TimeZone, etc.).
/// </summary>
public class BusinessOptions
{
    public const string SectionName = "Business";

    /// <summary>Zona horaria IANA del negocio (ej. "America/New_York").</summary>
    public string TimeZone { get; set; } = "America/New_York";

    /// <summary>Cada cuántos minutos empieza un hueco reservable (ej. 15 → 9:00, 9:15, 9:30...).</summary>
    public int SlotIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Citas simultáneas que el negocio puede atender cuando la cita no tiene
    /// estilista asignado (número de sillas/estilistas disponibles).
    /// </summary>
    public int MaxConcurrentAppointments { get; set; } = 1;

    /// <summary>Horario de apertura. Un día que no aparece en la lista está cerrado.</summary>
    public List<DaySchedule> OpeningHours { get; set; } = new();

    /// <summary>Devuelve el horario de un día, o null si el negocio está cerrado ese día.</summary>
    public DaySchedule? GetScheduleFor(DayOfWeek day) =>
        OpeningHours.FirstOrDefault(d => d.Day == day);

    /// <summary>Resuelve <see cref="TimeZone"/> a un <see cref="TimeZoneInfo"/>.</summary>
    public TimeZoneInfo GetTimeZoneInfo() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
}

/// <summary>Horario de un día de la semana, en hora local del negocio.</summary>
public class DaySchedule
{
    public DayOfWeek Day { get; set; }
    public TimeOnly Open { get; set; }
    public TimeOnly Close { get; set; }
}
