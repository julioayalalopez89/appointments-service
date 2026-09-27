using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Appointments.Api.Tests;

public class AppointmentsControllerTests
{
    [Fact]
    public void Capacity2_AcceptsTwoOverlapping_ThirdReturns409()
    {
        var controller = TestData.Controller(new InMemoryAppointmentRepository(), maxConcurrent: 2);

        var first = controller.Create(TestData.Request(TestData.Tuesday(10, 0)));
        var second = controller.Create(TestData.Request(TestData.Tuesday(10, 15)));
        var third = controller.Create(TestData.Request(TestData.Tuesday(10, 20)));

        Assert.IsType<CreatedAtActionResult>(first.Result);
        Assert.IsType<CreatedAtActionResult>(second.Result);
        Assert.IsType<ConflictObjectResult>(third.Result);
    }

    [Fact]
    public void Capacity1_WithoutStylist_SecondOverlappingReturns409()
    {
        var controller = TestData.Controller(new InMemoryAppointmentRepository(), maxConcurrent: 1);

        Assert.IsType<CreatedAtActionResult>(controller.Create(TestData.Request(TestData.Tuesday(10, 0))).Result);
        Assert.IsType<ConflictObjectResult>(controller.Create(TestData.Request(TestData.Tuesday(10, 0))).Result);
    }

    [Fact]
    public void CancelledAppointments_DoNotUseCapacity()
    {
        var repository = new InMemoryAppointmentRepository();
        var controller = TestData.Controller(repository, maxConcurrent: 1);

        var created = (CreatedAtActionResult)controller.Create(TestData.Request(TestData.Tuesday(10, 0))).Result!;
        controller.Cancel(((Appointment)created.Value!).Id, null);

        Assert.IsType<CreatedAtActionResult>(controller.Create(TestData.Request(TestData.Tuesday(10, 0))).Result);
    }

    [Theory]
    [InlineData(8, 30, 30)]   // antes de abrir (9:00)
    [InlineData(18, 45, 30)]  // termina después de cerrar (19:00)
    [InlineData(19, 0, 30)]   // empieza justo al cerrar
    public void OutsideOpeningHours_Returns400(int hour, int minute, int duration)
    {
        var controller = TestData.Controller(new InMemoryAppointmentRepository());

        var result = controller.Create(TestData.Request(TestData.Tuesday(hour, minute), duration));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void ClosedDay_Returns400()
    {
        var controller = TestData.Controller(new InMemoryAppointmentRepository());
        var wednesday = TestData.Tuesday(10).AddDays(1); // miércoles: no está en el horario de los tests

        var result = controller.Create(TestData.Request(wednesday));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void InThePast_Returns400()
    {
        var controller = TestData.Controller(new InMemoryAppointmentRepository());
        var lastTuesday = TestData.Tuesday(10).AddDays(-7);

        var result = controller.Create(TestData.Request(lastTuesday));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void LastSlotEndingAtClose_IsAccepted()
    {
        var controller = TestData.Controller(new InMemoryAppointmentRepository());

        var result = controller.Create(TestData.Request(TestData.Tuesday(18, 30), 30));

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public void WithStylist_KeepsPerStylistRule()
    {
        var controller = TestData.Controller(new InMemoryAppointmentRepository(), maxConcurrent: 1);

        Assert.IsType<CreatedAtActionResult>(controller.Create(TestData.Request(TestData.Tuesday(10), provider: "Ana")).Result);
        // Otro estilista a la misma hora: permitido.
        Assert.IsType<CreatedAtActionResult>(controller.Create(TestData.Request(TestData.Tuesday(10), provider: "Luis")).Result);
        // Mismo estilista solapado: 409.
        Assert.IsType<ConflictObjectResult>(controller.Create(TestData.Request(TestData.Tuesday(10, 15), provider: "ana")).Result);
    }
}
