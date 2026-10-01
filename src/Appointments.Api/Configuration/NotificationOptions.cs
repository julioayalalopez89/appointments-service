namespace Appointments.Api.Configuration;

/// <summary>
/// Avisos por email al salón (sección <c>Notifications</c>). Se envían con Resend.
/// Ni la API key ni el email del salón van en el repo: en Azure se definen con
/// <c>Notifications__ResendApiKey</c> (como secret) y <c>Notifications__SalonEmail</c>.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Dirección que recibe los avisos de citas nuevas y canceladas.</summary>
    public string? SalonEmail { get; set; }

    /// <summary>Remitente, p. ej. <c>305 Hair Style &lt;reservas@305hairstyle.com&gt;</c>. El dominio debe estar verificado en Resend.</summary>
    public string? FromEmail { get; set; }

    /// <summary>API key de Resend (<c>re_...</c>).</summary>
    public string? ResendApiKey { get; set; }

    /// <summary>Enlace a la agenda privada que se incluye en cada aviso.</summary>
    public string AdminUrl { get; set; } = "https://305hairstyle.com/admin/";

    /// <summary>True si hay todo lo necesario para enviar. Si no, los avisos se omiten con un aviso en el log.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SalonEmail) &&
        !string.IsNullOrWhiteSpace(FromEmail) &&
        !string.IsNullOrWhiteSpace(ResendApiKey);
}
