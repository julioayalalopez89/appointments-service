using Appointments.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Data;

public class AppointmentsDbContext : DbContext
{
    public AppointmentsDbContext(DbContextOptions<AppointmentsDbContext> options) : base(options)
    {
    }

    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Ignore(a => a.EndTime); // computed in C#, not a real column
            entity.Property(a => a.CustomerName).IsRequired().HasMaxLength(200);
            entity.Property(a => a.CustomerPhone).IsRequired().HasMaxLength(30);
            entity.Property(a => a.CustomerEmail).HasMaxLength(200);
            entity.Property(a => a.ServiceName).IsRequired().HasMaxLength(200);
            entity.Property(a => a.ProviderName).HasMaxLength(200);
            entity.Property(a => a.Notes).HasMaxLength(1000);
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        });
    }
}