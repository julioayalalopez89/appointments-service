using Appointments.Api.Data;
using Appointments.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Repositories;

/// <summary>
/// Almacenamiento real y persistente usando EF Core contra Azure SQL.
/// Reemplaza a InMemoryAppointmentRepository — misma interfaz, así que
/// el controlador no necesitó ningún cambio.
/// </summary>
public class EfAppointmentRepository : IAppointmentRepository
{
    private readonly AppointmentsDbContext _db;

    public EfAppointmentRepository(AppointmentsDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<Appointment> GetAll(DateOnly? onDate = null, AppointmentStatus? status = null)
    {
        var query = _db.Appointments.AsQueryable();

        if (onDate is { } date)
        {
            var start = date.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(a => a.StartTime.UtcDateTime >= start && a.StartTime.UtcDateTime < end);
        }

        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        return query.OrderBy(a => a.StartTime).ToList();
    }

    public Appointment? GetById(Guid id) => _db.Appointments.Find(id);

    public Appointment Add(Appointment appointment)
    {
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        return appointment;
    }

    public bool Update(Appointment appointment)
    {
        var existing = _db.Appointments.Find(appointment.Id);
        if (existing is null) return false;

        _db.Entry(existing).CurrentValues.SetValues(appointment);
        _db.SaveChanges();
        return true;
    }

    public bool Delete(Guid id)
    {
        var existing = _db.Appointments.Find(id);
        if (existing is null) return false;

        _db.Appointments.Remove(existing);
        _db.SaveChanges();
        return true;
    }

    public IReadOnlyList<Appointment> GetOverlapping(DateTimeOffset start, DateTimeOffset end, Guid? excludingId = null)
    {
        return _db.Appointments
            .Where(a =>
                a.Id != excludingId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartTime < end &&
                start < a.StartTime.AddMinutes(a.DurationMinutes))
            .ToList();
    }
}
