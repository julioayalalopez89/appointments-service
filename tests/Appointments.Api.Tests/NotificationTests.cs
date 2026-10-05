using System.Net;
using System.Text.Json;
using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Appointments.Api.Repositories;
using Appointments.Api.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Tests;

public class NotificationTests
{
    private static NotificationOptions Configured() => new()
    {
        SalonEmail = "salon@example.com",
        FromEmail = "305 Hair Style <reservas@example.com>",
        ResendApiKey = "re_test",
    };

    private static Appointment Booked(DateTimeOffset start) => (Appointment)
        ((CreatedAtActionResult)TestData.Controller(new InMemoryAppointmentRepository())
            .Create(TestData.Request(start)).Result!).Value!;

    // --- Controlador: crear / cancelar avisan al salón, y un fallo no rompe la reserva ---

    [Fact]
    public void Create_NotifiesSalon()
    {
        var notifications = new FakeNotificationService();
        var controller = TestData.Controller(new InMemoryAppointmentRepository(), notifications: notifications);

        var result = controller.Create(TestData.Request(TestData.Tuesday(10, 0)));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Same(created.Value, Assert.Single(notifications.Booked));
    }

    [Fact]
    public void Create_Rejected_DoesNotNotify()
    {
        var notifications = new FakeNotificationService();
        var controller = TestData.Controller(new InMemoryAppointmentRepository(), notifications: notifications);

        controller.Create(TestData.Request(TestData.Tuesday(8, 0))); // antes de abrir → 400

        Assert.Empty(notifications.Booked);
    }

    [Fact]
    public void Create_WhenNotificationFails_StillSavesAndReturns201()
    {
        var repository = new InMemoryAppointmentRepository();
        var controller = TestData.Controller(repository, notifications: new FakeNotificationService { Throw = true });

        var result = controller.Create(TestData.Request(TestData.Tuesday(10, 0)));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.NotNull(repository.GetById(((Appointment)created.Value!).Id));
    }

    [Fact]
    public void Cancel_NotifiesSalonWithReason_OnlyOnce()
    {
        var notifications = new FakeNotificationService();
        var controller = TestData.Controller(new InMemoryAppointmentRepository(), notifications: notifications);
        var created = (Appointment)((CreatedAtActionResult)controller.Create(TestData.Request(TestData.Tuesday(10, 0))).Result!).Value!;

        controller.Cancel(created.Id, new CancelAppointmentRequest { Reason = "Se enfermó" });
        controller.Cancel(created.Id, null); // ya estaba cancelada: no se vuelve a avisar

        var (appointment, reason) = Assert.Single(notifications.Cancelled);
        Assert.Equal(created.Id, appointment.Id);
        Assert.Equal("Se enfermó", reason);
    }

    [Fact]
    public void Cancel_WhenNotificationFails_StillCancels()
    {
        var repository = new InMemoryAppointmentRepository();
        var created = (Appointment)((CreatedAtActionResult)TestData.Controller(repository)
            .Create(TestData.Request(TestData.Tuesday(10, 0))).Result!).Value!;
        var controller = TestData.Controller(repository, notifications: new FakeNotificationService { Throw = true });

        var result = controller.Cancel(created.Id, null);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(AppointmentStatus.Cancelled, repository.GetById(created.Id)!.Status);
    }

    // --- EmailNotificationService: sin configuración no envía; con configuración encola ---

    [Fact]
    public void EmailService_WithoutApiKey_QueuesNothing()
    {
        var queue = new EmailQueue();
        var options = Configured();
        options.ResendApiKey = "";
        var service = new EmailNotificationService(Options.Create(options), Options.Create(TestData.Business()),
            queue, NullLogger<EmailNotificationService>.Instance);

        service.AppointmentBooked(Booked(TestData.Tuesday(10, 0)));

        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void EmailService_Configured_QueuesEmailToSalon()
    {
        var queue = new EmailQueue();
        var service = new EmailNotificationService(Options.Create(Configured()), Options.Create(TestData.Business()),
            queue, NullLogger<EmailNotificationService>.Instance);

        service.AppointmentCancelled(Booked(TestData.Tuesday(10, 0)), "No puede venir");

        Assert.True(queue.Reader.TryRead(out var message));
        Assert.Equal("salon@example.com", message!.To);
        Assert.Equal("305 Hair Style <reservas@example.com>", message.From);
        Assert.StartsWith("Cita cancelada: Test Customer", message.Subject);
        Assert.Contains("No puede venir", message.Text);
    }

    // --- Confirmación a la clienta ---

    private static List<EmailMessage> Drain(EmailQueue queue)
    {
        var messages = new List<EmailMessage>();
        while (queue.Reader.TryRead(out var message)) messages.Add(message);
        return messages;
    }

    private static EmailNotificationService Service(EmailQueue queue, NotificationOptions options) =>
        new(Options.Create(options), Options.Create(TestData.Business()), queue, NullLogger<EmailNotificationService>.Instance);

    [Fact]
    public void Booked_WithCustomerEmail_QueuesSalonNoticeAndCustomerConfirmation()
    {
        var queue = new EmailQueue();
        var appointment = Booked(TestData.Tuesday(10, 0));
        appointment.CustomerEmail = " ana@example.com ";

        Service(queue, Configured()).AppointmentBooked(appointment);

        var messages = Drain(queue);
        Assert.Equal(2, messages.Count);
        Assert.Equal("salon@example.com", messages[0].To);
        Assert.Null(messages[0].ReplyTo);
        Assert.Equal("ana@example.com", messages[1].To);
        Assert.Equal("305 Hair Style <reservas@example.com>", messages[1].From);
        Assert.Equal("salon@example.com", messages[1].ReplyTo);
        Assert.StartsWith("Tu cita en 305 Hair Style está confirmada", messages[1].Subject);
    }

    [Fact]
    public void Booked_WithoutCustomerEmail_OnlyNotifiesSalon()
    {
        var queue = new EmailQueue();

        Service(queue, Configured()).AppointmentBooked(Booked(TestData.Tuesday(10, 0)));

        Assert.Equal("salon@example.com", Assert.Single(Drain(queue)).To);
    }

    [Fact]
    public void Booked_WithoutSalonEmail_StillConfirmsToCustomer()
    {
        var queue = new EmailQueue();
        var options = Configured();
        options.SalonEmail = "";
        var appointment = Booked(TestData.Tuesday(10, 0));
        appointment.CustomerEmail = "ana@example.com";

        Service(queue, options).AppointmentBooked(appointment);

        var message = Assert.Single(Drain(queue));
        Assert.Equal("ana@example.com", message.To);
        Assert.Null(message.ReplyTo);
    }

    [Fact]
    public void Booked_WithoutApiKey_DoesNotConfirmToCustomer()
    {
        var queue = new EmailQueue();
        var options = Configured();
        options.ResendApiKey = "";
        var appointment = Booked(TestData.Tuesday(10, 0));
        appointment.CustomerEmail = "ana@example.com";

        Service(queue, options).AppointmentBooked(appointment);

        Assert.Empty(Drain(queue));
    }

    [Fact]
    public void Cancelled_DoesNotEmailCustomer()
    {
        var queue = new EmailQueue();
        var appointment = Booked(TestData.Tuesday(10, 0));
        appointment.CustomerEmail = "ana@example.com";

        Service(queue, Configured()).AppointmentCancelled(appointment, null);

        Assert.Equal("salon@example.com", Assert.Single(Drain(queue)).To);
    }

    [Fact]
    public void CustomerComposer_IncludesDateTimeServiceAddressAndWhatsApp()
    {
        var appointment = Booked(TestData.Tuesday(10, 0));
        appointment.StartTime = appointment.StartTime.ToUniversalTime(); // 14:00 UTC → 10:00 AM en Nueva York
        appointment.CustomerName = "Ana López";
        appointment.ProviderName = "Yuli";
        appointment.Notes = "Nota interna";
        var options = Configured();
        options.SalonAddress = "8631 Coral Wy, Miami, FL 33155";
        options.SalonMapsUrl = "https://maps.example.com/salon";
        options.SalonWhatsApp = "(786) 566-9938";

        var content = CustomerEmailComposer.Confirmation(appointment, TestData.Business(), options);

        Assert.Equal("Tu cita en 305 Hair Style está confirmada — martes 29 de septiembre de 2026, 10:00 AM", content.Subject);
        Assert.Contains("¡Hola, Ana!", content.Text);
        Assert.Contains("Fecha: martes 29 de septiembre de 2026", content.Text);
        Assert.Contains("Hora: 10:00 AM – 10:30 AM", content.Text);
        Assert.Contains("Servicio: Haircut", content.Text);
        Assert.Contains("Estilista: Yuli", content.Text);
        Assert.Contains("Dirección: 8631 Coral Wy, Miami, FL 33155", content.Text);
        Assert.Contains("href=\"https://maps.example.com/salon\"", content.Html);
        Assert.Contains("https://wa.me/17865669938?text=", content.Text);
        Assert.Contains("https://wa.me/17865669938?text=", content.Html);
        // Nada interno del salón en el email de la clienta.
        Assert.DoesNotContain("Nota interna", content.Text);
        Assert.DoesNotContain("/admin/", content.Html);
    }

    [Fact]
    public void CustomerComposer_EncodesHtml_AndFallsBackToReplyWithoutWhatsApp()
    {
        var appointment = Booked(TestData.Tuesday(10, 0));
        appointment.CustomerName = "<script>x</script>";
        appointment.ServiceName = "Corte & <b>color</b>";

        var content = CustomerEmailComposer.Confirmation(appointment, TestData.Business(), Configured());

        Assert.DoesNotContain("<script>", content.Html);
        Assert.Contains("Corte &amp; &lt;b&gt;color&lt;/b&gt;", content.Html);
        Assert.Contains("Responde a este email", content.Text);
        Assert.DoesNotContain("wa.me", content.Text);
        Assert.DoesNotContain("Dirección", content.Text);
    }

    // --- Contenido del email ---

    [Fact]
    public void Composer_UsesSalonLocalTime_AndIncludesContactLinks()
    {
        var appointment = Booked(TestData.Tuesday(10, 0));
        appointment.StartTime = appointment.StartTime.ToUniversalTime(); // 14:00 UTC → 10:00 AM en Nueva York
        appointment.CustomerPhone = "(305) 555-0100";
        appointment.CustomerEmail = "ana@example.com";
        appointment.Notes = "<b>Pelo largo</b>";

        var content = SalonEmailComposer.Booked(appointment, TestData.Business(), "https://305hairstyle.com/admin/");

        Assert.Equal("Nueva cita: Test Customer — martes 29 septiembre, 10:00 AM", content.Subject);
        Assert.Contains("martes 29 de septiembre de 2026", content.Text);
        Assert.Contains("10:00 AM – 10:30 AM", content.Text);
        Assert.Contains("Servicio: Haircut", content.Text);
        Assert.Contains("Duración: 30 min", content.Text);
        Assert.Contains("href=\"tel:3055550100\"", content.Html);
        Assert.Contains("https://wa.me/13055550100", content.Html);
        Assert.Contains("mailto:ana@example.com", content.Html);
        Assert.Contains("&lt;b&gt;Pelo largo&lt;/b&gt;", content.Html);
        Assert.DoesNotContain("<b>Pelo largo</b>", content.Html);
        Assert.Contains("https://305hairstyle.com/admin/", content.Html);
    }

    [Theory]
    [InlineData("+1 (305) 555-0100", "https://wa.me/13055550100")]
    [InlineData("305-555-0100", "https://wa.me/13055550100")]
    [InlineData("+34 600 123 456", "https://wa.me/34600123456")]
    [InlineData("n/a", null)]
    public void WhatsAppLink_NormalizesNumbers(string phone, string? expected)
    {
        Assert.Equal(expected, SalonEmailComposer.WhatsAppLink(phone));
    }

    // --- ResendEmailSender: llamada HTTP correcta y error si Resend rechaza ---

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        public RecordingHandler(HttpStatusCode status) => _status = status;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent("{\"message\":\"nope\"}") };
        }
    }

    private static readonly EmailMessage Message = new("from@example.com", "salon@example.com", "Asunto", "<p>Hola</p>", "Hola");

    [Fact]
    public async Task ResendSender_PostsJsonWithBearerKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var sender = new ResendEmailSender(new HttpClient(handler), Options.Create(Configured()));

        await sender.SendAsync(Message, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(ResendEmailSender.Endpoint, handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("re_test", handler.Request.Headers.Authorization.Parameter);

        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("from@example.com", json.RootElement.GetProperty("from").GetString());
        Assert.Equal("salon@example.com", json.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Asunto", json.RootElement.GetProperty("subject").GetString());
        Assert.Equal("<p>Hola</p>", json.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public async Task ResendSender_SendsReplyTo_OnlyWhenSet()
    {
        var withReply = new RecordingHandler(HttpStatusCode.OK);
        await new ResendEmailSender(new HttpClient(withReply), Options.Create(Configured()))
            .SendAsync(Message with { ReplyTo = "salon@example.com" }, CancellationToken.None);
        using (var json = JsonDocument.Parse(withReply.Body!))
        {
            Assert.Equal("salon@example.com", json.RootElement.GetProperty("reply_to").GetString());
        }

        var withoutReply = new RecordingHandler(HttpStatusCode.OK);
        await new ResendEmailSender(new HttpClient(withoutReply), Options.Create(Configured()))
            .SendAsync(Message, CancellationToken.None);
        using (var json = JsonDocument.Parse(withoutReply.Body!))
        {
            Assert.False(json.RootElement.TryGetProperty("reply_to", out _));
        }
    }

    [Fact]
    public async Task ResendSender_WhenRejected_Throws()
    {
        var sender = new ResendEmailSender(new HttpClient(new RecordingHandler(HttpStatusCode.Forbidden)), Options.Create(Configured()));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(Message, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }
}
