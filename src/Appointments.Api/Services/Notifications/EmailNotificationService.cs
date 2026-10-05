using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Prepara el aviso al salón y la confirmación a la clienta y los deja en <see cref="EmailQueue"/>;
/// el envío real lo hace <see cref="EmailNotificationWorker"/> en segundo plano. Sin configuración no envía nada.
/// </summary>
public sealed class EmailNotificationService : INotificationService
{
    private readonly NotificationOptions _options;
    private readonly BusinessOptions _business;
    private readonly EmailQueue _queue;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IOptions<NotificationOptions> options,
        IOptions<BusinessOptions> business,
        EmailQueue queue,
        ILogger<EmailNotificationService> logger)
    {
        _options = options.Value;
        _business = business.Value;
        _queue = queue;
        _logger = logger;
    }

    public void AppointmentBooked(Appointment appointment)
    {
        NotifySalon(appointment, "booked", () => SalonEmailComposer.Booked(appointment, _business, _options.AdminUrl));
        ConfirmToCustomer(appointment);
    }

    public void AppointmentCancelled(Appointment appointment, string? reason) =>
        NotifySalon(appointment, "cancelled", () => SalonEmailComposer.Cancelled(appointment, _business, _options.AdminUrl, reason));

    /// <summary>Confirmación a la clienta, solo si dejó email. Las respuestas van al email del salón.</summary>
    private void ConfirmToCustomer(Appointment appointment)
    {
        if (string.IsNullOrWhiteSpace(appointment.CustomerEmail))
        {
            return;
        }

        if (!_options.CanSend)
        {
            _logger.LogWarning(
                "Customer confirmation not sent for appointment {AppointmentId}: set Notifications__ResendApiKey " +
                "and Notifications__FromEmail to enable emails.",
                appointment.Id);
            return;
        }

        var content = CustomerEmailComposer.Confirmation(appointment, _business, _options);
        var replyTo = string.IsNullOrWhiteSpace(_options.SalonEmail) ? null : _options.SalonEmail;
        var message = new EmailMessage(
            _options.FromEmail!, appointment.CustomerEmail.Trim(), content.Subject, content.Html, content.Text, replyTo);

        if (!_queue.TryEnqueue(message))
        {
            _logger.LogError("Email queue is full; dropped customer confirmation for appointment {AppointmentId}.",
                appointment.Id);
        }
    }

    private void NotifySalon(Appointment appointment, string eventName, Func<SalonEmailComposer.Content> compose)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning(
                "Salon email not sent for appointment {AppointmentId} ({Event}): set Notifications__ResendApiKey, " +
                "Notifications__SalonEmail and Notifications__FromEmail to enable notifications.",
                appointment.Id, eventName);
            return;
        }

        // Se compone ahora (no en segundo plano) para enviar los datos tal como están en este momento.
        var content = compose();
        var message = new EmailMessage(_options.FromEmail!, _options.SalonEmail!, content.Subject, content.Html, content.Text);

        if (!_queue.TryEnqueue(message))
        {
            _logger.LogError("Salon email queue is full; dropped notification for appointment {AppointmentId} ({Event}).",
                appointment.Id, eventName);
        }
    }
}
