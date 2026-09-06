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

    /// <summary>True if the given time range overlaps an existing, non-cancelled appointment for the same provider.</summary>
    bool HasConflict(string? providerName, DateTimeOffset start, DateTimeOffset end, Guid? excludingId = null);
}
