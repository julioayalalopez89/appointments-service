using System.Globalization;
using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Appointments.Api.Security;
using Appointments.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Controllers;

/// <summary>
/// Huecos libres para reservar desde la web. Es público a propósito (la web lo
/// llama sin credenciales), por eso lleva [AllowAnonymous].
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Availability)]
public class AvailabilityController : ControllerBase
{
    private readonly IAppointmentRepository _repository;
    private readonly BusinessOptions _business;
    private readonly TimeProvider _timeProvider;

    public AvailabilityController(IAppointmentRepository repository, IOptions<BusinessOptions> business, TimeProvider timeProvider)
    {
        _repository = repository;
        _business = business.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>Free start times for an appointment without a stylist on the given day.</summary>
    /// <remarks>
    /// Uses the business opening hours, slot interval and capacity (Business section of the config).
    /// Past times (when the date is today) and slots that would end after closing are excluded.
    /// A closed day returns an empty list.
    ///
    ///     GET /api/availability?date=2026-10-03&amp;durationMinutes=60
    /// </remarks>
    /// <param name="date">Day in the business time zone, yyyy-MM-dd.</param>
    /// <param name="durationMinutes">Appointment length in minutes (5-480, default 30).</param>
    [HttpGet]
    [ProducesResponseType(typeof(AvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<AvailabilityResponse> Get([FromQuery] DateOnly? date, [FromQuery] int durationMinutes = 30)
    {
        if (date is not { } day)
        {
            return BadRequest(new { message = "Query parameter 'date' (yyyy-MM-dd) is required." });
        }

        if (durationMinutes is < 5 or > 480)
        {
            return BadRequest(new { message = "durationMinutes must be between 5 and 480." });
        }

        var (dayStart, dayEnd) = BookingRules.GetDayBounds(_business, day);
        var appointments = _repository.GetOverlapping(dayStart, dayEnd);
        var slots = BookingRules.GetAvailableSlots(_business, day, durationMinutes, _timeProvider.GetUtcNow(), appointments);

        return Ok(new AvailabilityResponse
        {
            Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeZone = _business.TimeZone,
            Slots = slots.Select(s => s.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList(),
        });
    }
}
