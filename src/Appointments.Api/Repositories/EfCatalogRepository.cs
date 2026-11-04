using Appointments.Api.Data;
using Appointments.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Repositories;

public class EfCatalogRepository : ICatalogRepository
{
    private readonly AppointmentsDbContext _db;

    public EfCatalogRepository(AppointmentsDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<CatalogService> GetServices() =>
        _db.CatalogServices.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();

    public IReadOnlyList<CatalogService> ReplaceServices(IReadOnlyList<CatalogService> services)
    {
        var existing = _db.CatalogServices.ToDictionary(s => s.Id);
        var keep = new HashSet<Guid>();

        foreach (var incoming in services)
        {
            if (existing.TryGetValue(incoming.Id, out var row))
            {
                row.Category = incoming.Category;
                row.Name = incoming.Name;
                row.DurationMinutes = incoming.DurationMinutes;
                row.Price = incoming.Price;
                row.Description = incoming.Description;
                row.Popular = incoming.Popular;
                row.Active = incoming.Active;
                row.SortOrder = incoming.SortOrder;
            }
            else
            {
                _db.CatalogServices.Add(incoming);
            }

            keep.Add(incoming.Id);
        }

        _db.CatalogServices.RemoveRange(existing.Values.Where(s => !keep.Contains(s.Id)));
        _db.SaveChanges();
        return GetServices();
    }

    public IReadOnlyList<BusinessDay> GetBusinessDays() =>
        _db.BusinessDays.AsNoTracking().OrderBy(d => d.Day).ToList();

    public IReadOnlyList<ClosedDate> GetClosedDates(DateOnly? from = null)
    {
        var query = _db.ClosedDates.AsNoTracking();
        if (from is { } f)
        {
            query = query.Where(d => d.Date >= f);
        }

        return query.OrderBy(d => d.Date).ToList();
    }

    public void ReplaceSchedule(IReadOnlyList<BusinessDay> days, IReadOnlyList<ClosedDate> closedDates)
    {
        _db.BusinessDays.RemoveRange(_db.BusinessDays);
        _db.ClosedDates.RemoveRange(_db.ClosedDates);
        _db.SaveChanges();

        _db.BusinessDays.AddRange(days);
        _db.ClosedDates.AddRange(closedDates);
        _db.SaveChanges();
    }
}
