using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Appointments.Api.Services;

namespace Appointments.Api.Tests;

/// <summary>
/// Servicios y horario editables desde el panel (/api/catalog). Arranca la API con
/// <see cref="ApiFactory"/>: horario de configuración martes y sábado, "ahora" = lunes 28/09/2026.
/// </summary>
public class CatalogTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CatalogServiceDto Service(string name, int minutes = 60, string price = "$55", bool active = true) => new()
    {
        Category = "Cabello",
        Name = name,
        DurationMinutes = minutes,
        Price = price,
        Description = "Descripción",
        Active = active,
    };

    private static List<BusinessDayDto> Week(params (DayOfWeek Day, string Open, string Close)[] open) =>
        Enum.GetValues<DayOfWeek>().Select(d =>
        {
            var o = open.FirstOrDefault(x => x.Day == d);
            return new BusinessDayDto { Day = (int)d, IsOpen = o.Open is not null, Open = o.Open ?? "09:00", Close = o.Close ?? "19:00" };
        }).ToList();

    // ---------- Seguridad ----------

    [Theory]
    [InlineData("GET", "/api/catalog/services")]
    [InlineData("PUT", "/api/catalog/services")]
    [InlineData("GET", "/api/catalog/schedule")]
    [InlineData("PUT", "/api/catalog/schedule")]
    public async Task ManagementEndpoints_RequireApiKey(string method, string url)
    {
        using var factory = new ApiFactory();
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT") request.Content = JsonContent.Create(new { });

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- GET /api/catalog ----------

    [Fact]
    public async Task Catalog_WithNothingSaved_ReturnsConfigScheduleAndNoServices()
    {
        using var factory = new ApiFactory();

        var catalog = await factory.CreateClient().GetFromJsonAsync<CatalogResponse>("/api/catalog", Json);

        Assert.NotNull(catalog);
        Assert.Empty(catalog.Services);
        Assert.False(catalog.Schedule.Customized);
        Assert.Equal("America/New_York", catalog.Schedule.TimeZone);
        Assert.Equal(7, catalog.Schedule.Days.Count);
        Assert.Equal(new[] { 2, 6 }, catalog.Schedule.Days.Where(d => d.IsOpen).Select(d => d.Day));
        Assert.Equal("17:00", catalog.Schedule.Days.Single(d => d.Day == 6).Close);
    }

    // ---------- Servicios ----------

    [Fact]
    public async Task PutServices_SavesInOrder_AndPublicCatalogHidesInactive()
    {
        using var factory = new ApiFactory();
        var admin = factory.CreateAdminClient();

        var put = await admin.PutAsJsonAsync("/api/catalog/services",
            new[] { Service("Corte"), Service("Color", 150, "$180+"), Service("Oculto", active: false) }, Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = (await put.Content.ReadFromJsonAsync<List<CatalogServiceDto>>(Json))!;
        Assert.Equal(new[] { "Corte", "Color", "Oculto" }, saved.Select(s => s.Name));
        Assert.All(saved, s => Assert.NotNull(s.Id));

        var all = await admin.GetFromJsonAsync<List<CatalogServiceDto>>("/api/catalog/services", Json);
        Assert.Equal(3, all!.Count);

        var publicCatalog = await factory.CreateClient().GetFromJsonAsync<CatalogResponse>("/api/catalog", Json);
        Assert.Equal(new[] { "Corte", "Color" }, publicCatalog!.Services.Select(s => s.Name));
        Assert.Equal("$180+", publicCatalog.Services[1].Price);
        Assert.Equal(150, publicCatalog.Services[1].DurationMinutes);
    }

    [Fact]
    public async Task PutServices_UpdatesByIdAndDeletesMissing()
    {
        using var factory = new ApiFactory();
        var admin = factory.CreateAdminClient();
        var first = (await (await admin.PutAsJsonAsync("/api/catalog/services", new[] { Service("Corte"), Service("Color") }, Json))
            .Content.ReadFromJsonAsync<List<CatalogServiceDto>>(Json))!;

        var corte = first.Single(s => s.Name == "Corte");
        corte.Name = "Corte + Peinado";
        corte.Price = "$60";
        var second = (await (await admin.PutAsJsonAsync("/api/catalog/services", new[] { corte, Service("Keratina") }, Json))
            .Content.ReadFromJsonAsync<List<CatalogServiceDto>>(Json))!;

        Assert.Equal(new[] { "Corte + Peinado", "Keratina" }, second.Select(s => s.Name));
        Assert.Equal(corte.Id, second[0].Id);
        Assert.Equal("$60", second[0].Price);
    }

    [Fact]
    public async Task PutServices_RejectsDuplicatedNames()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateAdminClient().PutAsJsonAsync("/api/catalog/services",
            new[] { Service("Corte"), Service(" corte ") }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Duplicated", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutServices_RejectsInvalidDuration()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateAdminClient().PutAsJsonAsync("/api/catalog/services", new[] { Service("Corte", minutes: 2) }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Horario ----------

    [Fact]
    public async Task PutSchedule_ChangesAvailabilityAndBooking()
    {
        using var factory = new ApiFactory();
        var admin = factory.CreateAdminClient();
        var client = factory.CreateClient();

        // Martes cerrado, miércoles 10:00-12:00.
        var put = await admin.PutAsJsonAsync("/api/catalog/schedule",
            new UpdateScheduleRequest { Days = Week((DayOfWeek.Wednesday, "10:00", "12:00")) }, Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var schedule = (await put.Content.ReadFromJsonAsync<ScheduleDto>(Json))!;
        Assert.True(schedule.Customized);
        Assert.Equal(new[] { 3 }, schedule.Days.Where(d => d.IsOpen).Select(d => d.Day));

        var tuesday = await client.GetFromJsonAsync<AvailabilityResponse>("/api/availability?date=2026-09-29&durationMinutes=60", Json);
        Assert.Empty(tuesday!.Slots);

        var wednesday = await client.GetFromJsonAsync<AvailabilityResponse>("/api/availability?date=2026-09-30&durationMinutes=60", Json);
        Assert.Equal("10:00", wednesday!.Slots.First());
        Assert.Equal("11:00", wednesday.Slots.Last());

        var booking = await client.PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(10)), Json);
        Assert.Equal(HttpStatusCode.BadRequest, booking.StatusCode);
    }

    [Fact]
    public async Task PutSchedule_AllDaysClosed_StaysClosed_InsteadOfFallingBackToConfig()
    {
        using var factory = new ApiFactory();

        await factory.CreateAdminClient().PutAsJsonAsync("/api/catalog/schedule", new UpdateScheduleRequest { Days = Week() }, Json);

        var tuesday = await factory.CreateClient().GetFromJsonAsync<AvailabilityResponse>("/api/availability?date=2026-09-29", Json);
        Assert.Empty(tuesday!.Slots);
    }

    [Fact]
    public async Task ClosedDate_BlocksThatDayOnly_AndPastOnesAreHiddenFromPublicCatalog()
    {
        using var factory = new ApiFactory();
        var admin = factory.CreateAdminClient();
        var client = factory.CreateClient();

        var put = await admin.PutAsJsonAsync("/api/catalog/schedule", new UpdateScheduleRequest
        {
            Days = Week((DayOfWeek.Tuesday, "09:00", "19:00"), (DayOfWeek.Saturday, "09:00", "17:00")),
            ClosedDates =
            {
                new ClosedDateDto { Date = new DateOnly(2026, 9, 29), Reason = "Vacaciones" },
                new ClosedDateDto { Date = new DateOnly(2026, 9, 1), Reason = "Pasado" },
            },
        }, Json);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var closedTuesday = await client.GetFromJsonAsync<AvailabilityResponse>("/api/availability?date=2026-09-29", Json);
        Assert.Empty(closedTuesday!.Slots);
        var nextTuesday = await client.GetFromJsonAsync<AvailabilityResponse>("/api/availability?date=2026-10-06", Json);
        Assert.NotEmpty(nextTuesday!.Slots);

        var booking = await client.PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(10)), Json);
        Assert.Equal(HttpStatusCode.BadRequest, booking.StatusCode);

        var publicCatalog = await client.GetFromJsonAsync<CatalogResponse>("/api/catalog", Json);
        var date = Assert.Single(publicCatalog!.Schedule.ClosedDates);
        Assert.Equal(new DateOnly(2026, 9, 29), date.Date);

        var adminSchedule = await admin.GetFromJsonAsync<ScheduleDto>("/api/catalog/schedule", Json);
        Assert.Equal(2, adminSchedule!.ClosedDates.Count);
    }

    [Theory]
    [InlineData("12:00", "10:00")] // cierre antes de abrir
    [InlineData("9:00", "18:00")]  // formato inválido (falta el 0)
    public async Task PutSchedule_RejectsInvalidHours(string open, string close)
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateAdminClient().PutAsJsonAsync("/api/catalog/schedule",
            new UpdateScheduleRequest { Days = Week((DayOfWeek.Tuesday, open, close)) }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Reglas puras ----------

    [Fact]
    public void BookingRules_ClosedDate_ReturnsNoSlotsAndRejectsBooking()
    {
        var options = TestData.Business();
        options.ClosedDates.Add(new DateOnly(2026, 9, 29));

        Assert.Empty(BookingRules.GetAvailableSlots(options, new DateOnly(2026, 9, 29), 30, TestData.Now, Array.Empty<Appointment>()));
        Assert.Contains("closed on 2026-09-29", BookingRules.ValidateSchedule(options, TestData.Tuesday(10), 30, TestData.Now));
    }
}
