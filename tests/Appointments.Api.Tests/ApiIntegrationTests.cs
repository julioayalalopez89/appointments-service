using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Appointments.Api.Models;

namespace Appointments.Api.Tests;

/// <summary>
/// Tests de integración: peticiones HTTP reales contra la API arrancada con
/// <see cref="ApiFactory"/>. Al menos un test por endpoint. Cada test usa su
/// propia fábrica, así que empieza con la base de datos vacía.
/// </summary>
public class ApiIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task<Appointment> BookAsync(HttpClient client, DateTimeOffset start, string? provider = null)
    {
        var response = await client.PostAsJsonAsync("/api/appointments", TestData.Request(start, provider: provider), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Appointment>(Json))!;
    }

    // ---------- GET / y GET /healthz ----------

    [Fact]
    public async Task Root_ReturnsServiceInfo()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Appointments.Api", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Healthz_ReturnsHealthy()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    // ---------- GET /api/availability ----------

    [Fact]
    public async Task Availability_ListsSlots_AndHidesBookedOnes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var before = await client.GetFromJsonAsync<AvailabilityResponse>("/api/availability?date=2026-09-29", Json);
        Assert.NotNull(before);
        Assert.Equal("2026-09-29", before.Date);
        Assert.Contains("10:00", before.Slots);

        await BookAsync(client, TestData.Tuesday(10));

        var after = await client.GetFromJsonAsync<AvailabilityResponse>("/api/availability?date=2026-09-29", Json);
        Assert.DoesNotContain("10:00", after!.Slots);
        Assert.Contains("10:30", after.Slots);
    }

    [Fact]
    public async Task Availability_WithoutDate_Returns400()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/availability");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- POST /api/appointments (público) ----------

    [Fact]
    public async Task Create_IsPublic_AndReturns201WithLocation()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(11)), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Appointment>(Json);
        Assert.Equal(AppointmentStatus.Booked, created!.Status);
        Assert.EndsWith($"/api/Appointments/{created.Id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_OnClosedDay_Returns400()
    {
        using var factory = new ApiFactory();
        // Miércoles 30/09/2026: no está en el horario de los tests.
        var wednesday = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(-4));

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/appointments", TestData.Request(wednesday), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_OverlappingSlot_Returns409()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await BookAsync(client, TestData.Tuesday(12));

        var response = await client.PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(12, 15)), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidPayload_Returns400()
    {
        using var factory = new ApiFactory();
        var request = TestData.Request(TestData.Tuesday(13));
        request.CustomerName = "";

        var response = await factory.CreateClient().PostAsJsonAsync("/api/appointments", request, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- GET /api/appointments ----------

    [Fact]
    public async Task List_WithoutApiKey_Returns401()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/appointments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WithApiKey_ReturnsBookedAppointments()
    {
        using var factory = new ApiFactory();
        var booked = await BookAsync(factory.CreateClient(), TestData.Tuesday(14));

        var list = await factory.CreateAdminClient().GetFromJsonAsync<List<Appointment>>("/api/appointments", Json);

        var only = Assert.Single(list!);
        Assert.Equal(booked.Id, only.Id);
    }

    // ---------- GET /api/appointments/{id} ----------

    [Fact]
    public async Task GetById_ReturnsAppointment_Or404()
    {
        using var factory = new ApiFactory();
        var booked = await BookAsync(factory.CreateClient(), TestData.Tuesday(15));
        var admin = factory.CreateAdminClient();

        var found = await admin.GetFromJsonAsync<Appointment>($"/api/appointments/{booked.Id}", Json);
        Assert.Equal("Test Customer", found!.CustomerName);

        var missing = await admin.GetAsync($"/api/appointments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GetById_WithoutApiKey_Returns401()
    {
        using var factory = new ApiFactory();
        var booked = await BookAsync(factory.CreateClient(), TestData.Tuesday(15));

        var response = await factory.CreateClient().GetAsync($"/api/appointments/{booked.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- PUT /api/appointments/{id} ----------

    [Fact]
    public async Task Update_ReschedulesAppointment()
    {
        using var factory = new ApiFactory();
        var booked = await BookAsync(factory.CreateClient(), TestData.Tuesday(9));
        var update = new UpdateAppointmentRequest
        {
            CustomerName = booked.CustomerName,
            CustomerPhone = booked.CustomerPhone,
            ServiceName = booked.ServiceName,
            StartTime = TestData.Tuesday(16),
            DurationMinutes = 45,
            Notes = "Moved to the afternoon",
        };

        var response = await factory.CreateAdminClient().PutAsJsonAsync($"/api/appointments/{booked.Id}", update, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<Appointment>(Json);
        Assert.Equal(TestData.Tuesday(16), updated!.StartTime);
        Assert.Equal(45, updated.DurationMinutes);
        Assert.Equal("Moved to the afternoon", updated.Notes);
    }

    // ---------- POST /api/appointments/{id}/cancel ----------

    [Fact]
    public async Task Cancel_MarksAsCancelled_AndFreesTheSlot()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var booked = await BookAsync(client, TestData.Tuesday(17));

        var response = await factory.CreateAdminClient()
            .PostAsJsonAsync($"/api/appointments/{booked.Id}/cancel", new CancelAppointmentRequest { Reason = "Client called" }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<Appointment>(Json);
        Assert.Equal(AppointmentStatus.Cancelled, cancelled!.Status);

        // El hueco vuelve a estar libre.
        await BookAsync(client, TestData.Tuesday(17));
    }

    // ---------- DELETE /api/appointments/{id} ----------

    [Fact]
    public async Task Delete_RemovesAppointment()
    {
        using var factory = new ApiFactory();
        var booked = await BookAsync(factory.CreateClient(), TestData.Tuesday(18));
        var admin = factory.CreateAdminClient();

        var response = await admin.DeleteAsync($"/api/appointments/{booked.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var again = await admin.DeleteAsync($"/api/appointments/{booked.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }
}
