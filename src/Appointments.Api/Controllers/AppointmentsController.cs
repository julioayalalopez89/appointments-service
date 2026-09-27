using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Appointments.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentRepository _repository;
    private readonly ILogger<AppointmentsController> _logger;
    private readonly BusinessOptions _business;
    private readonly TimeProvider _timeProvider;

    public AppointmentsController(
        IAppointmentRepository repository,
        ILogger<AppointmentsController> logger,
        IOptions<BusinessOptions> business,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _logger = logger;
        _business = business.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>List appointments, optionally filtered by date and/or status.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<Appointment>> GetAll([FromQuery] DateOnly? date, [FromQuery] AppointmentStatus? status)
    {
        return Ok(_repository.GetAll(date, status));
    }

    /// <summary>Get a single appointment by id.</summary>
    [HttpGet("{id:guid}")]
    public ActionResult<Appointment> GetById(Guid id)
    {
        var appointment = _repository.GetById(id);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    /// <summary>Book a new appointment.</summary>
    [HttpPost]
    public ActionResult<Appointment> Create([FromBody] CreateAppointmentRequest request)
    {
        if (request.DurationMinutes <= 0)
        {
            return ValidationProblem("DurationMinutes must be positive.");
        }

        var scheduleError = BookingRules.ValidateSchedule(_business, request.StartTime, request.DurationMinutes, _timeProvider.GetUtcNow());
        if (scheduleError is not null)
        {
            return BadRequest(new { message = scheduleError });
        }

        var end = request.StartTime.AddMinutes(request.DurationMinutes);
        if (HasConflict(request.ProviderName, request.StartTime, end))
        {
            return Conflict(new { message = ConflictMessage(request.ProviderName) });
        }

        var appointment = new Appointment
        {
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
            CustomerEmail = request.CustomerEmail,
            ServiceName = request.ServiceName,
            ProviderName = request.ProviderName,
            StartTime = request.StartTime,
            DurationMinutes = request.DurationMinutes,
            Notes = request.Notes,
        };

        _repository.Add(appointment);
        _logger.LogInformation("Booked appointment {AppointmentId} for {CustomerName} at {StartTime}",
            appointment.Id, appointment.CustomerName, appointment.StartTime);

        return CreatedAtAction(nameof(GetById), new { id = appointment.Id }, appointment);
    }

    /// <summary>Reschedule or edit an existing appointment.</summary>
    [HttpPut("{id:guid}")]
    public ActionResult<Appointment> Update(Guid id, [FromBody] UpdateAppointmentRequest request)
    {
        var existing = _repository.GetById(id);
        if (existing is null) return NotFound();

        // Solo se valida el horario si cambia la hora: así se pueden seguir
        // editando notas o datos del cliente de una cita ya pasada.
        var timeChanged = existing.StartTime != request.StartTime || existing.DurationMinutes != request.DurationMinutes;
        if (timeChanged)
        {
            var scheduleError = BookingRules.ValidateSchedule(_business, request.StartTime, request.DurationMinutes, _timeProvider.GetUtcNow());
            if (scheduleError is not null)
            {
                return BadRequest(new { message = scheduleError });
            }
        }

        var end = request.StartTime.AddMinutes(request.DurationMinutes);
        if (HasConflict(request.ProviderName, request.StartTime, end, excludingId: id))
        {
            return Conflict(new { message = ConflictMessage(request.ProviderName) });
        }

        existing.CustomerName = request.CustomerName;
        existing.CustomerPhone = request.CustomerPhone;
        existing.CustomerEmail = request.CustomerEmail;
        existing.ServiceName = request.ServiceName;
        existing.ProviderName = request.ProviderName;
        existing.StartTime = request.StartTime;
        existing.DurationMinutes = request.DurationMinutes;
        existing.Notes = request.Notes;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        _repository.Update(existing);
        return Ok(existing);
    }

    /// <summary>Cancel an appointment (soft delete — keeps history instead of removing the record).</summary>
    [HttpPost("{id:guid}/cancel")]
    public ActionResult<Appointment> Cancel(Guid id, [FromBody] CancelAppointmentRequest? request)
    {
        var existing = _repository.GetById(id);
        if (existing is null) return NotFound();

        existing.Status = AppointmentStatus.Cancelled;
        existing.Notes = string.IsNullOrWhiteSpace(request?.Reason)
            ? existing.Notes
            : $"{existing.Notes}\nCancelled: {request!.Reason}".Trim();
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        _repository.Update(existing);
        return Ok(existing);
    }

    /// <summary>Permanently delete an appointment record. Prefer /cancel for normal use.</summary>
    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        return _repository.Delete(id) ? NoContent() : NotFound();
    }

    private bool HasConflict(string? providerName, DateTimeOffset start, DateTimeOffset end, Guid? excludingId = null)
    {
        var overlapping = _repository.GetOverlapping(start, end, excludingId);
        return BookingRules.HasConflict(_business, providerName, start, end, overlapping);
    }

    private static string ConflictMessage(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName)
            ? "No availability at this time: the salon is fully booked for that slot."
            : $"{providerName} already has an appointment that overlaps this time.";
}
