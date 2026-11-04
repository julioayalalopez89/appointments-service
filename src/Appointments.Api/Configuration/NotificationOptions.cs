namespace Appointments.Api.Configuration;

/// <summary>
/// Emails de las citas (sección <c>Notifications</c>): avisos al salón y confirmación a la
/// clienta. Se envían con Resend. Ni la API key ni el email del salón van en el repo: en Azure
/// se definen con <c>Notifications__ResendApiKey</c> (como secret) y <c>Notifications__SalonEmail</c>.
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

    /// <summary>Nombre del salón en el asunto y la firma de la confirmación a la clienta.</summary>
    public string SalonName { get; set; } = "305 Hair Style";

    /// <summary>Dirección del salón que se incluye en la confirmación a la clienta.</summary>
    public string? SalonAddress { get; set; }

    /// <summary>Enlace a Google Maps de la dirección (opcional).</summary>
    public string? SalonMapsUrl { get; set; }

    /// <summary>Número de WhatsApp del salón (p. ej. <c>+1 786 566 9938</c>) para cambios o cancelaciones.</summary>
    public string? SalonWhatsApp { get; set; }

    /// <summary>True si se puede enviar algún email (API key y remitente).</summary>
    public bool CanSend =>
        !string.IsNullOrWhiteSpace(FromEmail) &&
        !string.IsNullOrWhiteSpace(ResendApiKey);

    /// <summary>True si hay todo lo necesario para avisar al salón. Si no, los avisos se omiten con un aviso en el log.</summary>
    public bool IsConfigured => CanSend && !string.IsNullOrWhiteSpace(SalonEmail);
}
