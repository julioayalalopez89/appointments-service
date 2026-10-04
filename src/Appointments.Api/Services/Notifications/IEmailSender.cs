namespace Appointments.Api.Services.Notifications;

/// <summary>Envía un email. Lanza una excepción si el proveedor lo rechaza.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
