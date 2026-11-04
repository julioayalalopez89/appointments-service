using System.Net;
using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Appointments.Api.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Tests;

/// <summary>Avisos al salón por WhatsApp con CallMeBot.</summary>
public class WhatsAppNotificationTests
{
    private static NotificationOptions Configured() => new()
    {
        WhatsAppPhone = "+1 (786) 566-9938",
        CallMeBotApiKey = "123456",
    };

    private static Appointment Appt(string? notes = null, string? email = null) => new()
    {
        CustomerName = "Ana  Pérez",
        CustomerPhone = "(305) 555-0100",
        CustomerEmail = email,
        ServiceName = "Corte + Peinado",
        StartTime = TestData.Tuesday(10, 0),
        DurationMinutes = 60,
        Notes = notes,
    };

    private static WhatsAppNotificationService Service(WhatsAppQueue queue, NotificationOptions options) =>
        new(Options.Create(options), Options.Create(TestData.Business()), queue, NullLogger<WhatsAppNotificationService>.Instance);

    [Fact]
    public void Booked_Text_HasCustomerServiceAndLocalTime()
    {
        var text = WhatsAppComposer.Booked(Appt(notes: "Pelo largo\nrubio", email: "ana@example.com"), TestData.Business(), "https://305hairstyle.com/admin/");

        Assert.StartsWith("📅 *Nueva cita*", text);
        Assert.Contains("Ana Pérez", text);
        Assert.Contains("Corte + Peinado", text);
        Assert.Contains("martes 29 de septiembre de 2026, 10:00 AM – 11:00 AM", text);
        Assert.Contains("https://wa.me/13055550100", text);
        Assert.Contains("ana@example.com", text);
        Assert.Contains("📝 Pelo largo rubio", text);
        Assert.EndsWith("Agenda: https://305hairstyle.com/admin/", text);
    }

    [Fact]
    public void Cancelled_Text_IncludesReason()
    {
        var text = WhatsAppComposer.Cancelled(Appt(), TestData.Business(), "https://x/admin/", "No puede venir");

        Assert.StartsWith("❌ *Cita cancelada*", text);
        Assert.Contains("Motivo: No puede venir", text);
    }

    [Fact]
    public void BuildUrl_EncodesPhoneTextAndKey()
    {
        var url = CallMeBotWhatsAppSender.BuildUrl("+1 (786) 566-9938", " 123456 ", "Nueva cita\nAna & Co");

        Assert.Equal("https://api.callmebot.com/whatsapp.php?phone=%2B17865669938&text=Nueva%20cita%0AAna%20%26%20Co&apikey=123456", url);
    }

    [Theory]
    [InlineData("7865669938", "+17865669938")]
    [InlineData("+34 621 33 17 09", "+34621331709")]
    public void NormalizePhone_AddsUsPrefixForTenDigits(string input, string expected) =>
        Assert.Equal(expected, CallMeBotWhatsAppSender.NormalizePhone(input));

    [Fact]
    public void Service_WithoutConfig_EnqueuesNothing()
    {
        var queue = new WhatsAppQueue();
        Service(queue, new NotificationOptions()).AppointmentBooked(Appt());

        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void Service_WithConfig_EnqueuesBookedAndCancelled()
    {
        var queue = new WhatsAppQueue();
        var service = Service(queue, Configured());

        service.AppointmentBooked(Appt());
        service.AppointmentCancelled(Appt(), null);

        Assert.True(queue.Reader.TryRead(out var booked));
        Assert.Contains("Nueva cita", booked!.Text);
        Assert.True(queue.Reader.TryRead(out var cancelled));
        Assert.Contains("Cita cancelada", cancelled!.Text);
    }

    [Fact]
    public void Composite_OneChannelThrowing_DoesNotStopTheOthers()
    {
        var failing = new FakeNotificationService { Throw = true };
        var ok = new FakeNotificationService();
        var composite = new CompositeNotificationService(new INotificationService[] { failing, ok }, NullLogger<CompositeNotificationService>.Instance);

        composite.AppointmentBooked(Appt());

        Assert.Single(ok.Booked);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "Message queued. You will receive it in a few seconds.", false)]
    [InlineData(HttpStatusCode.OK, "<p><b>APIKey is invalid.</b></p>", true)]
    [InlineData(HttpStatusCode.BadRequest, "Bad request", true)]
    public async Task Sender_DetectsErrors_IncludingErrorsInA200Body(HttpStatusCode status, string body, bool shouldThrow)
    {
        var handler = new StubHandler(status, body);
        var sender = new CallMeBotWhatsAppSender(new HttpClient(handler), Options.Create(Configured()));

        var send = () => sender.SendAsync(new WhatsAppMessage("hola"), CancellationToken.None);

        if (shouldThrow) await Assert.ThrowsAsync<HttpRequestException>(send);
        else await send();
        Assert.StartsWith("https://api.callmebot.com/whatsapp.php?phone=%2B17865669938&text=hola&apikey=123456", handler.LastUrl);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
