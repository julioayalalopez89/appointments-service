using System.Globalization;
using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Appointments.Api.Security;
using Appointments.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Appointments.Api.Controllers;

/// <summary>
/// Servicios (precio, duración) y horario de reservas. La web lee <c>GET /api/catalog</c>;
/// el panel del negocio edita con los endpoints protegidos por <c>X-Api-Key</c>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CatalogController : ControllerBase
{
    public const int MaxServices = 100;
    public const int MaxClosedDates = 366;

    private readonly ICatalogRepository _catalog;
    private readonly IBusinessScheduleProvider _business;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogController> _logger;

    public CatalogController(ICatalogRepository catalog, IBusinessScheduleProvider business, TimeProvider timeProvider, ILogger<CatalogController> logger)
    {
        _catalog = catalog;
        _business = business;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Active services and the booking schedule (public, used by the website).</summary>
    /// <remarks>Only closed dates from today on are listed. An empty service list means none were saved yet.</remarks>
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(CatalogResponse), StatusCodes.Status200OK)]
    public ActionResult<CatalogResponse> Get()
    {
        return Ok(new CatalogResponse
        {
            Services = _catalog.GetServices().Where(s => s.Active).Select(ToDto).ToList(),
            Schedule = BuildSchedule(onlyUpcoming: true),
        });
    }

    /// <summary>All services, including hidden ones. Requires <c>X-Api-Key</c>.</summary>
    [RequireApiKey]
    [HttpGet("services")]
    public ActionResult<IReadOnlyList<CatalogServiceDto>> GetServices() =>
        Ok(_catalog.GetServices().Select(ToDto).ToList());

    /// <summary>Replace the whole service list (order = list order). Requires <c>X-Api-Key</c>.</summary>
    /// <remarks>Items with a known <c>id</c> are updated, the rest are created, and services missing from the list are deleted.</remarks>
    [RequireApiKey]
    [HttpPut("services")]
    [ProducesResponseType(typeof(IReadOnlyList<CatalogServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<CatalogServiceDto>> PutServices([FromBody] List<CatalogServiceDto> services)
    {
        if (services.Count > MaxServices)
        {
            return BadRequest(new { message = $"Too many services (max {MaxServices})." });
        }

        var normalized = services.Select(s => new CatalogServiceDto
        {
            Id = s.Id,
            Category = s.Category.Trim(),
            Name = s.Name.Trim(),
            DurationMinutes = s.DurationMinutes,
            Price = s.Price.Trim(),
            Description = string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim(),
            Popular = s.Popular,
            Active = s.Active,
        }).ToList();

        var blank = normalized.FirstOrDefault(s => s.Name.Length == 0 || s.Category.Length == 0 || s.Price.Length == 0);
        if (blank is not null)
        {
            return BadRequest(new { message = "Every service needs a name, a category and a price." });
        }

        // Las citas guardan el nombre del servicio: dos con el mismo nombre serían ambiguos.
        var duplicated = normalized.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            return BadRequest(new { message = $"Duplicated service name: \"{duplicated.Key}\"." });
        }

        var seenIds = new HashSet<Guid>();
        var entities = normalized.Select((s, i) => new CatalogService
        {
            // Un id repetido en la misma lista se trata como servicio nuevo.
            Id = s.Id is { } id && id != Guid.Empty && seenIds.Add(id) ? id : Guid.NewGuid(),
            Category = s.Category,
            Name = s.Name,
            DurationMinutes = s.DurationMinutes,
            Price = s.Price,
            Description = s.Description,
            Popular = s.Popular,
            Active = s.Active,
            SortOrder = i,
        }).ToList();

        var saved = _catalog.ReplaceServices(entities);
        _logger.LogInformation("Service catalog replaced: {Count} services", saved.Count);
        return Ok(saved.Select(ToDto).ToList());
    }

    /// <summary>Effective weekly schedule and all closed dates. Requires <c>X-Api-Key</c>.</summary>
    [RequireApiKey]
    [HttpGet("schedule")]
    public ActionResult<ScheduleDto> GetSchedule() => Ok(BuildSchedule(onlyUpcoming: false));

    /// <summary>Replace the weekly schedule (all 7 days) and the closed dates. Requires <c>X-Api-Key</c>.</summary>
    /// <remarks>Existing appointments are not touched, even if they now fall outside the new hours.</remarks>
    [RequireApiKey]
    [HttpPut("schedule")]
    [ProducesResponseType(typeof(ScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<ScheduleDto> PutSchedule([FromBody] UpdateScheduleRequest request)
    {
        var errors = new List<string>();

        var byDay = request.Days.GroupBy(d => d.Day).ToList();
        if (byDay.Any(g => g.Count() > 1))
        {
            errors.Add("Each day of the week can appear only once.");
        }

        var days = new List<BusinessDay>();
        foreach (var d in request.Days.DistinctBy(d => d.Day))
        {
            var open = TimeOnly.ParseExact(d.Open, "HH:mm", CultureInfo.InvariantCulture);
            var close = TimeOnly.ParseExact(d.Close, "HH:mm", CultureInfo.InvariantCulture);
            if (d.IsOpen && close <= open)
            {
                errors.Add($"{(DayOfWeek)d.Day}: closing time must be after opening time.");
            }

            days.Add(new BusinessDay { Day = (DayOfWeek)d.Day, IsOpen = d.IsOpen, Open = open, Close = close });
        }

        // Los días que no vienen quedan cerrados: así siempre hay 7 filas.
        foreach (var missing in Enum.GetValues<DayOfWeek>().Where(day => days.All(d => d.Day != day)))
        {
            days.Add(new BusinessDay { Day = missing, IsOpen = false, Open = new TimeOnly(9, 0), Close = new TimeOnly(19, 0) });
        }

        if (request.ClosedDates.Count > MaxClosedDates)
        {
            errors.Add($"Too many closed dates (max {MaxClosedDates}).");
        }

        if (errors.Count > 0)
        {
            return BadRequest(new { message = string.Join(" ", errors) });
        }

        var closed = request.ClosedDates
            .DistinctBy(c => c.Date)
            .Select(c => new ClosedDate { Date = c.Date, Reason = string.IsNullOrWhiteSpace(c.Reason) ? null : c.Reason.Trim() })
            .ToList();

        _catalog.ReplaceSchedule(days.OrderBy(d => d.Day).ToList(), closed);
        _logger.LogInformation("Schedule replaced: open {Days}; {Closed} closed dates",
            string.Join(",", days.Where(d => d.IsOpen).Select(d => d.Day)), closed.Count);

        return Ok(BuildSchedule(onlyUpcoming: false));
    }

    private ScheduleDto BuildSchedule(bool onlyUpcoming)
    {
        var options = _business.Current();
        var storedDays = _catalog.GetBusinessDays();
        var customized = storedDays.Count > 0;

        List<BusinessDayDto> days;
        if (customized)
        {
            days = storedDays.Select(d => new BusinessDayDto
            {
                Day = (int)d.Day,
                IsOpen = d.IsOpen,
                Open = d.Open.ToString("HH:mm", CultureInfo.InvariantCulture),
                Close = d.Close.ToString("HH:mm", CultureInfo.InvariantCulture),
            }).ToList();
        }
        else
        {
            // Sin horario guardado: el de la configuración, con 9-19 de muestra en los días cerrados.
            days = Enum.GetValues<DayOfWeek>().Select(day =>
            {
                var s = options.GetScheduleFor(day);
                return new BusinessDayDto
                {
                    Day = (int)day,
                    IsOpen = s is not null,
                    Open = (s?.Open ?? new TimeOnly(9, 0)).ToString("HH:mm", CultureInfo.InvariantCulture),
                    Close = (s?.Close ?? new TimeOnly(19, 0)).ToString("HH:mm", CultureInfo.InvariantCulture),
                };
            }).ToList();
        }

        DateOnly? from = null;
        if (onlyUpcoming)
        {
            var localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), options.GetTimeZoneInfo());
            from = DateOnly.FromDateTime(localNow.DateTime);
        }

        return new ScheduleDto
        {
            TimeZone = options.TimeZone,
            SlotIntervalMinutes = options.SlotIntervalMinutes,
            Days = days.OrderBy(d => d.Day).ToList(),
            ClosedDates = _catalog.GetClosedDates(from)
                .Select(c => new ClosedDateDto { Date = c.Date, Reason = c.Reason })
                .ToList(),
            Customized = customized,
        };
    }

    private static CatalogServiceDto ToDto(CatalogService s) => new()
    {
        Id = s.Id,
        Category = s.Category,
        Name = s.Name,
        DurationMinutes = s.DurationMinutes,
        Price = s.Price,
        Description = s.Description,
        Popular = s.Popular,
        Active = s.Active,
    };
}
