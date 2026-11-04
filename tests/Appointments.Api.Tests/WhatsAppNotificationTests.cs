using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Appointments.Api.Configuration;
using Appointments.Api.Controllers;
using Appointments.Api.Data;
using Appointments.Api.Models;
using Appointments.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Tests;

/// <summary>Avisos al salón por WhatsApp (API oficial de Meta) y su tope mensual.</summary>
public class WhatsAppNotificationTests
{
    private static NotificationOptions Configured(int limit = 100) => new()
    {
        WhatsAppTo = "+1 (786) 566-9938",
        WhatsAppPhoneNumberId = "1234567890",
        WhatsAppAccessToken = "EAAtoken",
        WhatsAppMonthlyLimit = limit,
    };

    private static Appointment Appt() => new()
    {
        CustomerName = "Ana  Pérez\nGómez",
        CustomerPhone = "(305) 555-0100",
        ServiceName = "Corte + Peinado",
        StartTime = TestData.Tuesday(10, 0),
        DurationMinutes = 60,
    };

    private static WhatsAppMessage Message() => new("nueva_cita", new[] { "Ana", "Corte", "martes", "305" });

    private static AppointmentsDbContext Db() =>
        new(new DbContextOptionsBuilder<AppointmentsDbContext>().UseInMemoryDatabase($"wa-{Guid.NewGuid()}").Options);

    private static WhatsAppDispatcher Dispatcher(AppointmentsDbContext db, IWhatsAppSender sender, int limit, DateTimeOffset? now = null) =>
        new(new EfNotificationUsageStore(db), sender, Options.Create(Configured(limit)), Options.Create(TestData.Business()),
            new FixedTimeProvider(now ?? TestData.Now), NullLogger<WhatsAppDispatcher>.Instance);

    // ---------- Texto de las variables ----------

    [Fact]
    public void Parameters_AreOneLineInOrder_WithLocalTime()
    {
        var p = WhatsAppComposer.Parameters(Appt(), TestData.Business());

        Assert.Equal(new[]
        {
            "Ana Pérez Gómez",
            "Corte + Peinado",
            "martes 29 de septiembre de 2026, 10:00 AM – 11:00 AM",
            "(305) 555-0100",
        }, p);
    }

    [Fact]
    public void Clean_EmptyBecomesDash_AndLongIsTruncated()
    {
        Assert.Equal("-", WhatsAppComposer.Clean("  "));
        Assert.Equal(10, WhatsAppComposer.Clean(new string('a', 50), 10).Length);
    }

    // ---------- Petición a Meta ----------

    [Fact]
    public async Task Sender_PostsTemplateToGraphApi_WithBearerToken()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"wamid.1\"}]}");
        var sender = new MetaWhatsAppSender(new HttpClient(handler), Options.Create(Configured()));

        await sender.SendAsync(Message(), CancellationToken.None);

        Assert.Equal("https://graph.facebook.com/v23.0/1234567890/messages", handler.Url);
        Assert.Equal("Bearer EAAtoken", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("17865669938", root.GetProperty("to").GetString());
        Assert.Equal("template", root.GetProperty("type").GetString());
        var template = root.GetProperty("template");
        Assert.Equal("nueva_cita", template.GetProperty("name").GetString());
        Assert.Equal("es", template.GetProperty("language").GetProperty("code").GetString());
        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal(4, parameters.GetArrayLength());
        Assert.Equal("Ana", parameters[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Sender_ThrowsWithMetaError()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Template name does not exist\"}}");
        var sender = new MetaWhatsAppSender(new HttpClient(handler), Options.Create(Configured()));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(Message(), CancellationToken.None));
        Assert.Contains("Template name does not exist", ex.Message);
    }

    // ---------- Tope mensual ----------

    [Fact]
    public async Task Dispatcher_StopsAtMonthlyLimit()
    {
        using var db = Db();
        var sender = new CountingSender();
        var dispatcher = Dispatcher(db, sender, limit: 2);

        Assert.True(await dispatcher.DispatchAsync(Message(), CancellationToken.None));
        Assert.True(await dispatcher.DispatchAsync(Message(), CancellationToken.None));
        Assert.False(await dispatcher.DispatchAsync(Message(), CancellationToken.None));

        Assert.Equal(2, sender.Count);
        Assert.Equal(2, await new EfNotificationUsageStore(db).GetCountAsync("whatsapp", "2026-09", CancellationToken.None));
    }

    [Fact]
    public async Task Dispatcher_ResetsNextMonth_InBusinessTimeZone()
    {
        using var db = Db();
        var sender = new CountingSender();
        // 30/09 23:30 en Nueva York (ya es 1/10 en UTC) sigue siendo septiembre.
        var lateSeptember = new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.FromHours(-4));
        Assert.True(await Dispatcher(db, sender, 1, lateSeptember).DispatchAsync(Message(), CancellationToken.None));
        Assert.False(await Dispatcher(db, sender, 1, lateSeptember).DispatchAsync(Message(), CancellationToken.None));

        var october = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(-4));
        Assert.True(await Dispatcher(db, sender, 1, october).DispatchAsync(Message(), CancellationToken.None));
        Assert.Equal(2, sender.Count);
    }

    [Fact]
    public async Task Dispatcher_FailedSendStillCounts_SoTheLimitIsNeverExceeded()
    {
        using var db = Db();
        var dispatcher = Dispatcher(db, new CountingSender { Throw = true }, limit: 1);

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.DispatchAsync(Message(), CancellationToken.None));
        Assert.False(await dispatcher.DispatchAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task Dispatcher_LimitZero_SendsNothing()
    {
        using var db = Db();
        var sender = new CountingSender();

        Assert.False(await Dispatcher(db, sender, 0).DispatchAsync(Message(), CancellationToken.None));
        Assert.Equal(0, sender.Count);
    }

    // ---------- Servicio y composición ----------

    [Fact]
    public void Service_WithoutConfig_EnqueuesNothing()
    {
        var queue = new WhatsAppQueue();
        new WhatsAppNotificationService(Options.Create(new NotificationOptions()), Options.Create(TestData.Business()), queue,
            NullLogger<WhatsAppNotificationService>.Instance).AppointmentBooked(Appt());

        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void Service_WithConfig_UsesBookedAndCancelledTemplates()
    {
        var queue = new WhatsAppQueue();
        var service = new WhatsAppNotificationService(Options.Create(Configured()), Options.Create(TestData.Business()), queue,
            NullLogger<WhatsAppNotificationService>.Instance);

        service.AppointmentBooked(Appt());
        service.AppointmentCancelled(Appt(), "motivo");

        Assert.True(queue.Reader.TryRead(out var booked));
        Assert.Equal("nueva_cita", booked!.Template);
        Assert.True(queue.Reader.TryRead(out var cancelled));
        Assert.Equal("cita_cancelada", cancelled!.Template);
    }

    [Fact]
    public void Composite_OneChannelThrowing_DoesNotStopTheOthers()
    {
        var ok = new FakeNotificationService();
        var composite = new CompositeNotificationService(
            new INotificationService[] { new FakeNotificationService { Throw = true }, ok }, NullLogger<CompositeNotificationService>.Instance);

        composite.AppointmentBooked(Appt());

        Assert.Single(ok.Booked);
    }

    // ---------- Endpoint de uso ----------

    [Fact]
    public async Task UsageEndpoint_RequiresKey_AndReportsMonthAndLimit()
    {
        using var factory = new ApiFactory();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/notifications/whatsapp")).StatusCode);

        var usage = await factory.CreateAdminClient().GetFromJsonAsync<WhatsAppUsageResponse>(
            "/api/notifications/whatsapp", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(usage);
        Assert.False(usage.Configured);
        Assert.Equal("2026-09", usage.Month);
        Assert.Equal(0, usage.Sent);
        Assert.Equal(100, usage.MonthlyLimit);
    }

    private sealed class CountingSender : IWhatsAppSender
    {
        public bool Throw { get; init; }
        public int Count { get; private set; }

        public Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
        {
            if (Throw) throw new HttpRequestException("Meta is down");
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
