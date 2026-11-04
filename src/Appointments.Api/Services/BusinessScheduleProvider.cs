using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Services;

/// <summary>Reglas del negocio vigentes: configuración + lo guardado desde el panel.</summary>
public interface IBusinessScheduleProvider
{
    BusinessOptions Current();

    /// <summary>true si el horario semanal sale de la base de datos (se guardó desde el panel).</summary>
    bool IsCustomized { get; }
}

/// <summary>
/// Parte de <see cref="BusinessOptions"/> (zona horaria, intervalo, capacidad) y, si el
/// horario se guardó desde el panel, sustituye <c>OpeningHours</c> por el de la base de
/// datos y añade los días cerrados. Sin nada guardado se comporta como antes.
/// </summary>
public class BusinessScheduleProvider : IBusinessScheduleProvider
{
    private readonly BusinessOptions _config;
    private readonly ICatalogRepository _catalog;
    private BusinessOptions? _current;
    private bool _customized;

    public BusinessScheduleProvider(IOptions<BusinessOptions> config, ICatalogRepository catalog)
    {
        _config = config.Value;
        _catalog = catalog;
    }

    public bool IsCustomized
    {
        get
        {
            Current();
            return _customized;
        }
    }

    // Scoped: se calcula una vez por petición.
    public BusinessOptions Current()
    {
        if (_current is not null) return _current;

        var days = _catalog.GetBusinessDays();
        _customized = days.Count > 0;

        _current = new BusinessOptions
        {
            TimeZone = _config.TimeZone,
            SlotIntervalMinutes = _config.SlotIntervalMinutes,
            MaxConcurrentAppointments = _config.MaxConcurrentAppointments,
            OpeningHours = _customized
                ? days.Where(d => d.IsOpen)
                      .Select(d => new DaySchedule { Day = d.Day, Open = d.Open, Close = d.Close })
                      .ToList()
                : _config.OpeningHours.ToList(),
            ClosedDates = _catalog.GetClosedDates().Select(d => d.Date).ToHashSet(),
        };
        return _current;
    }
}
