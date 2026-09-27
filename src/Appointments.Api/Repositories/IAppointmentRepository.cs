using Appointments.Api.Models;

namespace Appointments.Api.Repositories;

/// <summary>
/// Storage abstraction for appointments. The controller only depends on
/// this interface, so swapping InMemoryAppointmentRepository for an
/// EF Core + SQL/Postgres implementation later is a one-file change.
/// </summary>
public interface IAppointmentRepository
{
    IReadOnlyList<Appointment> GetAll(DateOnly? onDate = null, AppointmentStatus? status = null);
    Appointment? GetById(Guid id);
    Appointment Add(Appointment appointment);
    bool Update(Appointment appointment);
    bool Delete(Guid id);

    /// <summary>
    /// Non-cancelled appointments (any provider) that overlap [start, end).
    /// The conflict rules themselves live in <see cref="Services.BookingRules"/>.
    /// </summary>
    IReadOnlyList<Appointment> GetOverlapping(DateTimeOffset start, DateTimeOffset end, Guid? excludingId = null);
}
