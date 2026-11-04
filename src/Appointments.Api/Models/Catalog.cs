using System.ComponentModel.DataAnnotations;

namespace Appointments.Api.Models;

// Catálogo editable desde el panel del negocio: servicios con precio y duración,
// horario semanal y días cerrados puntuales. Mientras estas tablas estén vacías,
// la API usa el horario de la configuración (Business:OpeningHours) y la web sus
// servicios de respaldo, así que desplegar esto no cambia nada hasta que se guarda.

/// <summary>Un servicio que se puede reservar (corte, color...).</summary>
public class CatalogService
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Grupo en la web (ej. "Cabello", "Tratamientos").</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Nombre visible. Es lo que se guarda en <see cref="Appointment.ServiceName"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Lo que ocupa la cita en la agenda.</summary>
    public int DurationMinutes { get; set; } = 30;

    /// <summary>Precio tal como se muestra ("$55", "$180+", "Desde $40").</summary>
    public string Price { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Se marca como "Popular" en la web.</summary>
    public bool Popular { get; set; }

    /// <summary>Oculto en la web cuando es false (sin borrarlo).</summary>
    public bool Active { get; set; } = true;

    /// <summary>Orden en la web (ascendente).</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Horario de un día de la semana. Se guardan siempre los 7 días: si la tabla tiene
/// filas, manda la tabla (aunque todos estén cerrados); si está vacía, la configuración.
/// </summary>
public class BusinessDay
{
    public DayOfWeek Day { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly Open { get; set; }
    public TimeOnly Close { get; set; }
}

/// <summary>Día concreto en que no se atiende (festivo, vacaciones...).</summary>
public class ClosedDate
{
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
}

// ───────────── DTOs ─────────────

/// <summary>Servicio en las respuestas y en <c>PUT /api/catalog/services</c>.</summary>
public class CatalogServiceDto
{
    /// <summary>Id existente para editarlo; vacío o desconocido = servicio nuevo.</summary>
    public Guid? Id { get; set; }

    [Required, MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(5, 480)]
    public int DurationMinutes { get; set; } = 30;

    [Required, MaxLength(30)]
    public string Price { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool Popular { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>Horario de un día en las respuestas y en <c>PUT /api/catalog/schedule</c>.</summary>
public class BusinessDayDto
{
    /// <summary>0 = domingo … 6 = sábado (igual que <c>Date.getDay()</c> en JavaScript).</summary>
    [Range(0, 6)]
    public int Day { get; set; }

    public bool IsOpen { get; set; }

    /// <summary>Hora local del negocio, "HH:mm".</summary>
    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
    public string Open { get; set; } = "09:00";

    /// <summary>Hora local del negocio, "HH:mm".</summary>
    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
    public string Close { get; set; } = "19:00";
}

public class ClosedDateDto
{
    /// <summary>yyyy-MM-dd.</summary>
    [Required]
    public DateOnly Date { get; set; }

    [MaxLength(200)]
    public string? Reason { get; set; }
}

/// <summary>Cuerpo de <c>PUT /api/catalog/schedule</c>: los 7 días y los días cerrados.</summary>
public class UpdateScheduleRequest
{
    [Required]
    public List<BusinessDayDto> Days { get; set; } = new();

    public List<ClosedDateDto> ClosedDates { get; set; } = new();
}

/// <summary>Horario efectivo (lo que usa la disponibilidad).</summary>
public class ScheduleDto
{
    public string TimeZone { get; set; } = string.Empty;
    public int SlotIntervalMinutes { get; set; }

    /// <summary>Siempre 7 días, de domingo (0) a sábado (6).</summary>
    public List<BusinessDayDto> Days { get; set; } = new();

    public List<ClosedDateDto> ClosedDates { get; set; } = new();

    /// <summary>false = todavía sale de la configuración del servidor (nunca se guardó desde el panel).</summary>
    public bool Customized { get; set; }
}

/// <summary><c>GET /api/catalog</c>: todo lo que la web necesita para mostrar y reservar.</summary>
public class CatalogResponse
{
    /// <summary>Servicios activos, en orden. Vacío = el panel aún no tiene servicios (la web usa los suyos).</summary>
    public List<CatalogServiceDto> Services { get; set; } = new();

    public ScheduleDto Schedule { get; set; } = new();
}
