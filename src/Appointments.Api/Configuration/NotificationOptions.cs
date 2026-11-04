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

    // ───── WhatsApp (API oficial de Meta: WhatsApp Cloud API) ─────

    /// <summary>Número que recibe los avisos de citas, formato internacional (<c>+17865669938</c>). <c>Notifications__WhatsAppTo</c>.</summary>
    public string? WhatsAppTo { get; set; }

    /// <summary>"Phone number ID" del número que envía (Meta → WhatsApp → API Setup). <c>Notifications__WhatsAppPhoneNumberId</c>.</summary>
    public string? WhatsAppPhoneNumberId { get; set; }

    /// <summary>Token de acceso permanente (usuario del sistema). Secret: <c>Notifications__WhatsAppAccessToken</c>.</summary>
    public string? WhatsAppAccessToken { get; set; }

    /// <summary>Plantilla aprobada para citas nuevas (4 variables: clienta, servicio, fecha y hora, teléfono).</summary>
    public string WhatsAppBookedTemplate { get; set; } = "nueva_cita";

    /// <summary>Plantilla aprobada para citas canceladas (mismas 4 variables).</summary>
    public string WhatsAppCancelledTemplate { get; set; } = "cita_cancelada";

    /// <summary>Idioma con el que se aprobaron las plantillas (p. ej. <c>es</c>, <c>es_MX</c>).</summary>
    public string WhatsAppTemplateLanguage { get; set; } = "es";

    /// <summary>
    /// Máximo de WhatsApp por mes (en la zona horaria del negocio). Al llegar, se dejan de enviar
    /// hasta el mes siguiente y solo llegan los emails. 0 = no enviar ninguno.
    /// </summary>
    public int WhatsAppMonthlyLimit { get; set; } = 100;

    /// <summary>Versión de la Graph API de Meta.</summary>
    public string WhatsAppGraphVersion { get; set; } = "v23.0";

    /// <summary>True si hay destinatario, Phone number ID y token. Si no, no se envía ningún WhatsApp.</summary>
    public bool IsWhatsAppConfigured =>
        !string.IsNullOrWhiteSpace(WhatsAppTo) &&
        !string.IsNullOrWhiteSpace(WhatsAppPhoneNumberId) &&
        !string.IsNullOrWhiteSpace(WhatsAppAccessToken);

    /// <summary>True si se puede enviar algún email (API key y remitente).</summary>
    public bool CanSend =>
        !string.IsNullOrWhiteSpace(FromEmail) &&
        !string.IsNullOrWhiteSpace(ResendApiKey);

    /// <summary>True si hay todo lo necesario para avisar al salón. Si no, los avisos se omiten con un aviso en el log.</summary>
    public bool IsConfigured => CanSend && !string.IsNullOrWhiteSpace(SalonEmail);
}
