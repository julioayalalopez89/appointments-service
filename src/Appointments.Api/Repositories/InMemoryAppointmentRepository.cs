using System.Collections.Concurrent;
using Appointments.Api.Models;

namespace Appointments.Api.Repositories;

/// <summary>
/// Thread-safe in-memory store. Data does not survive a restart or a second
/// replica — that's fine for local dev and for a single-pod demo deploy, but
/// before running more than one Kubernetes replica in production, swap this
/// for a real database (see README "Next steps").
/// </summary>
public class InMemoryAppointmentRepository : IAppointmentRepository
{
    private readonly ConcurrentDictionary<Guid, Appointment> _store = new();

    public IReadOnlyList<Appointment> GetAll(DateOnly? onDate = null, AppointmentStatus? status = null)
    {
        var query = _store.Values.AsEnumerable();

        if (onDate is { } date)
        {
            query = query.Where(a => DateOnly.FromDateTime(a.StartTime.UtcDateTime) == date);
        }

        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        return query.OrderBy(a => a.StartTime).ToList();
    }

    public Appointment? GetById(Guid id) => _store.GetValueOrDefault(id);

    public Appointment Add(Appointment appointment)
    {
        _store[appointment.Id] = appointment;
        return appointment;
    }

    public bool Update(Appointment appointment)
    {
        if (!_store.ContainsKey(appointment.Id)) return false;
        _store[appointment.Id] = appointment;
        return true;
    }

    public bool Delete(Guid id) => _store.TryRemove(id, out _);

    public bool HasConflict(string? providerName, DateTimeOffset start, DateTimeOffset end, Guid? excludingId = null)
    {
        return _store.Values.Any(a =>
            a.Id != excludingId &&
            a.Status != AppointmentStatus.Cancelled &&
            string.Equals(a.ProviderName ?? string.Empty, providerName ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            a.StartTime < end &&
            start < a.EndTime);
    }
}
