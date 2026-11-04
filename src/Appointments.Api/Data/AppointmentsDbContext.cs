using Appointments.Api.Services.Notifications;
using Appointments.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Data;

public class AppointmentsDbContext : DbContext
{
    public AppointmentsDbContext(DbContextOptions<AppointmentsDbContext> options) : base(options)
    {
    }

    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<CatalogService> CatalogServices => Set<CatalogService>();
    public DbSet<BusinessDay> BusinessDays => Set<BusinessDay>();
    public DbSet<ClosedDate> ClosedDates => Set<ClosedDate>();
    public DbSet<NotificationUsage> NotificationUsage => Set<NotificationUsage>();

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

        modelBuilder.Entity<CatalogService>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.Property(s => s.Category).IsRequired().HasMaxLength(100);
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.Property(s => s.Price).IsRequired().HasMaxLength(30);
            entity.Property(s => s.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<BusinessDay>(entity =>
        {
            entity.HasKey(d => d.Day);
            entity.Property(d => d.Day).ValueGeneratedNever();
        });

        modelBuilder.Entity<NotificationUsage>(entity =>
        {
            entity.ToTable("NotificationUsage");
            entity.HasKey(u => new { u.Channel, u.Month });
            entity.Property(u => u.Channel).HasMaxLength(20);
            entity.Property(u => u.Month).HasMaxLength(7);
        });

        modelBuilder.Entity<ClosedDate>(entity =>
        {
            entity.HasKey(d => d.Date);
            entity.Property(d => d.Reason).HasMaxLength(200);
        });
    }
}