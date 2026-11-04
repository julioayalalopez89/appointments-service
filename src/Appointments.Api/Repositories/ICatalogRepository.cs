using Appointments.Api.Models;

namespace Appointments.Api.Repositories;

/// <summary>Servicios y horario editables desde el panel del negocio.</summary>
public interface ICatalogRepository
{
    /// <summary>Todos los servicios (también los inactivos), por <see cref="CatalogService.SortOrder"/>.</summary>
    IReadOnlyList<CatalogService> GetServices();

    /// <summary>Sustituye la lista completa: actualiza los que existen, crea los nuevos y borra los que faltan.</summary>
    IReadOnlyList<CatalogService> ReplaceServices(IReadOnlyList<CatalogService> services);

    /// <summary>Horario semanal guardado. Vacío = nunca se guardó (se usa la configuración).</summary>
    IReadOnlyList<BusinessDay> GetBusinessDays();

    /// <summary>Días cerrados desde <paramref name="from"/> (incluido), o todos si es null.</summary>
    IReadOnlyList<ClosedDate> GetClosedDates(DateOnly? from = null);

    /// <summary>Sustituye el horario semanal y la lista de días cerrados en una sola operación.</summary>
    void ReplaceSchedule(IReadOnlyList<BusinessDay> days, IReadOnlyList<ClosedDate> closedDates);
}
