namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Un email listo para enviar (HTML + texto plano). <paramref name="ReplyTo"/> es opcional:
/// en la confirmación a la clienta apunta al email del salón para que pueda responder.
/// </summary>
public sealed record EmailMessage(string From, string To, string Subject, string Html, string Text, string? ReplyTo = null);
