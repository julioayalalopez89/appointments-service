using Appointments.Api.Models;
using Appointments.Api.Services;

namespace Appointments.Api.Tests;

public class BookingRulesTests
{
    private static Appointment At(DateTimeOffset start, int minutes, string? provider = null) => new()
    {
        StartTime = start,
        DurationMinutes = minutes,
        ProviderName = provider,
    };

    [Fact]
    public void BackToBackAppointments_DoNotCountAsSimultaneous()
    {
        // 10:00-10:30 y 10:30-11:00 solapan con 10:00-11:00, pero nunca a la vez.
        var existing = new[]
        {
            At(TestData.Tuesday(10, 0), 30),
            At(TestData.Tuesday(10, 30), 30),
        };

        var conflict = BookingRules.HasConflict(
            TestData.Business(maxConcurrent: 2), null, TestData.Tuesday(10, 0), TestData.Tuesday(11, 0), existing);

        Assert.False(conflict);
    }

    [Fact]
    public void NoStylist_ConflictsWhenPeakReachesCapacity()
    {
        var existing = new[]
        {
            At(TestData.Tuesday(10, 0), 60),
            At(TestData.Tuesday(10, 30), 60, provider: "Ana"), // también ocupa una silla
        };

        var conflict = BookingRules.HasConflict(
            TestData.Business(maxConcurrent: 2), null, TestData.Tuesday(10, 45), TestData.Tuesday(11, 15), existing);

        Assert.True(conflict);
    }

    [Fact]
    public void ValidateSchedule_ReturnsNullForValidSlot()
    {
        var error = BookingRules.ValidateSchedule(TestData.Business(), TestData.Tuesday(9, 0), 60, TestData.Now);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateSchedule_UsesBusinessTimeZone()
    {
        // 13:30 UTC = 9:30 en Nueva York (EDT) → dentro de horario aunque en UTC parezca otra hora.
        var startUtc = new DateTimeOffset(2026, 9, 29, 13, 30, 0, TimeSpan.Zero);
        // 12:30 UTC = 8:30 en Nueva York → antes de abrir.
        var tooEarlyUtc = new DateTimeOffset(2026, 9, 29, 12, 30, 0, TimeSpan.Zero);

        Assert.Null(BookingRules.ValidateSchedule(TestData.Business(), startUtc, 30, TestData.Now));
        Assert.NotNull(BookingRules.ValidateSchedule(TestData.Business(), tooEarlyUtc, 30, TestData.Now));
    }
}
