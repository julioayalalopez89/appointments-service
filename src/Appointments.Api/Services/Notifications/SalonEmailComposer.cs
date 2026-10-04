using System.Globalization;
using System.Net;
using System.Text;
using Appointments.Api.Configuration;
using Appointments.Api.Models;

namespace Appointments.Api.Services.Notifications;

/// <summary>Texto de los avisos al salón (en español, con la hora local del negocio).</summary>
public static class SalonEmailComposer
{
    private static readonly string[] Days = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
    private static readonly string[] Months =
        { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

    public sealed record Content(string Subject, string Html, string Text);

    public static Content Booked(Appointment appointment, BusinessOptions business, string adminUrl) =>
        Compose(appointment, business, adminUrl, cancelled: false, reason: null);

    public static Content Cancelled(Appointment appointment, BusinessOptions business, string adminUrl, string? reason) =>
        Compose(appointment, business, adminUrl, cancelled: true, reason: reason);

    /// <summary>"martes 29 de septiembre de 2026" en la zona horaria del negocio.</summary>
    public static string FormatLocalDate(DateTimeOffset local) =>
        $"{Days[(int)local.DayOfWeek]} {local.Day} de {Months[local.Month - 1]} de {local.Year}";

    /// <summary>"10:30 AM" (12 h, como se lee en Miami).</summary>
    public static string FormatLocalTime(DateTimeOffset local) =>
        local.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>
    /// Enlace de WhatsApp (<c>https://wa.me/&lt;dígitos&gt;</c>). Un número de 10 dígitos se
    /// considera de EE. UU. y se le añade el prefijo 1. Null si no hay dígitos.
    /// </summary>
    public static string? WhatsAppLink(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0) return null;
        if (digits.Length == 10) digits = "1" + digits;
        return $"https://wa.me/{digits}";
    }

    private static Content Compose(Appointment a, BusinessOptions business, string adminUrl, bool cancelled, string? reason)
    {
        var tz = business.GetTimeZoneInfo();
        var start = TimeZoneInfo.ConvertTime(a.StartTime, tz);
        var end = TimeZoneInfo.ConvertTime(a.EndTime, tz);

        var date = FormatLocalDate(start);
        var time = $"{FormatLocalTime(start)} – {FormatLocalTime(end)}";
        var name = OneLine(a.CustomerName);

        var subject = cancelled
            ? $"Cita cancelada: {name} — {Days[(int)start.DayOfWeek]} {start.Day} {Months[start.Month - 1]}, {FormatLocalTime(start)}"
            : $"Nueva cita: {name} — {Days[(int)start.DayOfWeek]} {start.Day} {Months[start.Month - 1]}, {FormatLocalTime(start)}";
        var title = cancelled ? "Se ha cancelado una cita" : "Nueva cita reservada";

        var phone = a.CustomerPhone.Trim();
        var whatsApp = WhatsAppLink(phone);
        var telHref = "tel:" + new string(phone.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());

        // Filas (etiqueta, texto plano, HTML ya codificado).
        var rows = new List<(string Label, string Text, string Html)>
        {
            ("Fecha", date, Enc(date)),
            ("Hora (salón)", time, Enc(time)),
            ("Servicio", a.ServiceName, Enc(a.ServiceName)),
            ("Duración", $"{a.DurationMinutes} min", $"{a.DurationMinutes} min"),
        };
        if (!string.IsNullOrWhiteSpace(a.ProviderName))
        {
            rows.Add(("Estilista", a.ProviderName, Enc(a.ProviderName)));
        }
        rows.Add(("Cliente", a.CustomerName, Enc(a.CustomerName)));
        rows.Add(("Teléfono",
            whatsApp is null ? phone : $"{phone} (WhatsApp: {whatsApp})",
            $"<a href=\"{Enc(telHref)}\">{Enc(phone)}</a>" +
                (whatsApp is null ? "" : $" · <a href=\"{Enc(whatsApp)}\">WhatsApp</a>")));
        rows.Add(("Email",
            string.IsNullOrWhiteSpace(a.CustomerEmail) ? "—" : a.CustomerEmail,
            string.IsNullOrWhiteSpace(a.CustomerEmail) ? "—" : $"<a href=\"mailto:{Enc(a.CustomerEmail)}\">{Enc(a.CustomerEmail)}</a>"));
        rows.Add(("Notas",
            string.IsNullOrWhiteSpace(a.Notes) ? "—" : a.Notes,
            string.IsNullOrWhiteSpace(a.Notes) ? "—" : Enc(a.Notes).Replace("\n", "<br>")));
        if (cancelled && !string.IsNullOrWhiteSpace(reason))
        {
            rows.Add(("Motivo", reason, Enc(reason)));
        }

        var text = new StringBuilder();
        text.AppendLine(title).AppendLine();
        foreach (var row in rows) text.AppendLine($"{row.Label}: {row.Text}");
        text.AppendLine().AppendLine($"Agenda: {adminUrl}");

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#222\">");
        html.Append($"<h2 style=\"margin:0 0 12px\">{Enc(title)}</h2>");
        html.Append("<table cellpadding=\"6\" style=\"border-collapse:collapse\">");
        foreach (var row in rows)
        {
            html.Append($"<tr><td style=\"color:#666;vertical-align:top\">{Enc(row.Label)}</td><td><strong>{row.Html}</strong></td></tr>");
        }
        html.Append("</table>");
        html.Append($"<p style=\"margin-top:16px\"><a href=\"{Enc(adminUrl)}\">Abrir la agenda</a></p>");
        html.Append("</div>");

        return new Content(subject, html.ToString(), text.ToString());
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
