namespace Appointments.Api.Services.Notifications;

/// <summary>Un email listo para enviar (HTML + texto plano).</summary>
public sealed record EmailMessage(string From, string To, string Subject, string Html, string Text);
