using Appointments.Api.Controllers;
using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Appointments.Api.Tests;

public class AvailabilityControllerTests
{
    private static readonly DateOnly TuesdayDate = new(2026, 9, 29);

    private static AvailabilityResponse GetSlots(AvailabilityController controller, DateOnly date, int durationMinutes)
    {
        var result = controller.Get(date, durationMinutes);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<AvailabilityResponse>(ok.Value);
    }

    private static Appointment Booked(DateTimeOffset start, int minutes) => new()
    {
        CustomerName = "Existing",
        CustomerPhone = "+1 305 555 0101",
        ServiceName = "Haircut",
        StartTime = start,
        DurationMinutes = minutes,
    };

    [Fact]
    public void ClosedDay_ReturnsEmptySlots()
    {
        var controller = TestData.Availability(new InMemoryAppointmentRepository());

        var response = GetSlots(controller, TuesdayDate.AddDays(1), 60); // miércoles: cerrado en los tests

        Assert.Equal("2026-09-30", response.Date);
        Assert.Equal("America/New_York", response.TimeZone);
        Assert.Empty(response.Slots);
    }

    [Fact]
    public void EmptyDay_ReturnsAllSlotsThatFitBeforeClosing()
    {
        var controller = TestData.Availability(new InMemoryAppointmentRepository());

        var response = GetSlots(controller, TuesdayDate, 60);

        // 9:00-19:00 cada 15 min; el último que cabe con 60 min es 18:00 → 37 slots.
        Assert.Equal(37, response.Slots.Count);
        Assert.Equal("09:00", response.Slots[0]);
        Assert.Equal("18:00", response.Slots[^1]);
    }

    [Fact]
    public void FullDay_ReturnsEmptySlots()
    {
        var repository = new InMemoryAppointmentRepository();
        repository.Add(Booked(TestData.Tuesday(9, 0), 600)); // 9:00-19:00, capacidad 1
        var controller = TestData.Availability(repository, maxConcurrent: 1);

        var response = GetSlots(controller, TuesdayDate, 30);

        Assert.Empty(response.Slots);
    }

    [Fact]
    public void PartialDay_ExcludesSlotsThatOverlapBookings()
    {
        var repository = new InMemoryAppointmentRepository();
        repository.Add(Booked(TestData.Tuesday(10, 0), 60)); // 10:00-11:00
        var controller = TestData.Availability(repository, maxConcurrent: 1);

        var response = GetSlots(controller, TuesdayDate, 60);

        Assert.Contains("09:00", response.Slots);   // termina justo a las 10:00
        Assert.DoesNotContain("09:15", response.Slots);
        Assert.DoesNotContain("10:00", response.Slots);
        Assert.DoesNotContain("10:45", response.Slots);
        Assert.Contains("11:00", response.Slots);   // empieza justo al terminar
    }

    [Fact]
    public void PartialDay_WithCapacity2_OneBookingLeavesSlotFree()
    {
        var repository = new InMemoryAppointmentRepository();
        repository.Add(Booked(TestData.Tuesday(10, 0), 60));
        var controller = TestData.Availability(repository, maxConcurrent: 2);

        var response = GetSlots(controller, TuesdayDate, 60);

        Assert.Contains("10:00", response.Slots);
    }

    [Fact]
    public void CancelledBookings_DoNotBlockSlots()
    {
        var repository = new InMemoryAppointmentRepository();
        var cancelled = Booked(TestData.Tuesday(10, 0), 60);
        cancelled.Status = AppointmentStatus.Cancelled;
        repository.Add(cancelled);
        var controller = TestData.Availability(repository, maxConcurrent: 1);

        var response = GetSlots(controller, TuesdayDate, 60);

        Assert.Contains("10:00", response.Slots);
    }

    [Fact]
    public void Today_ExcludesPastTimes()
    {
        var now = TestData.Tuesday(12, 10); // martes 12:10
        var controller = TestData.Availability(new InMemoryAppointmentRepository(), now: now);

        var response = GetSlots(controller, TuesdayDate, 30);

        Assert.DoesNotContain("12:00", response.Slots);
        Assert.Equal("12:15", response.Slots[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(481)]
    public void InvalidDuration_Returns400(int durationMinutes)
    {
        var controller = TestData.Availability(new InMemoryAppointmentRepository());

        var result = controller.Get(TuesdayDate, durationMinutes);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void MissingDate_Returns400()
    {
        var controller = TestData.Availability(new InMemoryAppointmentRepository());

        var result = controller.Get(null, 30);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
