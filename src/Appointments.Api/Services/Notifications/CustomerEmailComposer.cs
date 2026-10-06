using System.Net;
using System.Text;
using Appointments.Api.Configuration;
using Appointments.Api.Models;

namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Confirmación de la reserva para la clienta (en español, con la hora local del salón):
/// fecha, hora, servicio, dirección y cómo cambiar o cancelar (WhatsApp). No incluye
/// nada interno del salón (ni la agenda ni las notas).
/// </summary>
public static class CustomerEmailComposer
{
    public static SalonEmailComposer.Content Confirmation(Appointment a, BusinessOptions business, NotificationOptions options)
    {
        var tz = business.GetTimeZoneInfo();
        var start = TimeZoneInfo.ConvertTime(a.StartTime, tz);
        var end = TimeZoneInfo.ConvertTime(a.EndTime, tz);

        var salon = OneLine(options.SalonName);
        var date = SalonEmailComposer.FormatLocalDate(start);
        var startTime = SalonEmailComposer.FormatLocalTime(start);
        var time = $"{startTime} – {SalonEmailComposer.FormatLocalTime(end)}";
        var firstName = FirstName(a.CustomerName);
        var service = OneLine(a.ServiceName);

        var subject = $"Tu cita en {salon} está confirmada — {date}, {startTime}";
        var greeting = firstName.Length == 0 ? "¡Hola!" : $"¡Hola, {firstName}!";
        var intro = $"Tu cita en {salon} está reservada. Te esperamos:";

        // Filas (etiqueta, texto plano, HTML ya codificado).
        var rows = new List<(string Label, string Text, string Html)>
        {
            ("Fecha", date, Enc(date)),
            ("Hora", time, Enc(time)),
            ("Servicio", service, Enc(service)),
        };
        if (!string.IsNullOrWhiteSpace(a.ProviderName))
        {
            rows.Add(("Estilista", a.ProviderName, Enc(a.ProviderName)));
        }
        if (!string.IsNullOrWhiteSpace(options.SalonAddress))
        {
            var address = options.SalonAddress.Trim();
            var maps = options.SalonMapsUrl?.Trim();
            rows.Add(("Dirección",
                string.IsNullOrWhiteSpace(maps) ? address : $"{address} ({maps})",
                string.IsNullOrWhiteSpace(maps) ? Enc(address) : $"<a href=\"{Enc(maps)}\">{Enc(address)}</a>"));
        }

        // Cambios o cancelaciones: WhatsApp con un mensaje ya escrito; si no hay número, responder al email.
        var whatsApp = string.IsNullOrWhiteSpace(options.SalonWhatsApp) ? null : SalonEmailComposer.WhatsAppLink(options.SalonWhatsApp);
        string changesText, changesHtml;
        if (whatsApp is not null)
        {
            var prefilled = $"Hola, quiero cambiar o cancelar mi cita del {date} a las {startTime} ({service}).";
            var link = $"{whatsApp}?text={Uri.EscapeDataString(prefilled)}";
            changesText = $"¿Necesitas cambiar o cancelar tu cita? Escríbenos por WhatsApp: {link}";
            changesHtml = $"¿Necesitas cambiar o cancelar tu cita? <a href=\"{Enc(link)}\">Escríbenos por WhatsApp</a>.";
        }
        else
        {
            changesText = "¿Necesitas cambiar o cancelar tu cita? Responde a este email.";
            changesHtml = Enc(changesText);
        }

        var text = new StringBuilder();
        text.AppendLine(greeting).AppendLine().AppendLine(intro).AppendLine();
        foreach (var row in rows) text.AppendLine($"{row.Label}: {row.Text}");
        text.AppendLine().AppendLine(changesText).AppendLine().AppendLine(salon);

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#222\">");
        html.Append($"<h2 style=\"margin:0 0 12px\">{Enc(greeting)}</h2>");
        html.Append($"<p style=\"margin:0 0 12px\">{Enc(intro)}</p>");
        html.Append("<table cellpadding=\"6\" style=\"border-collapse:collapse\">");
        foreach (var row in rows)
        {
            html.Append($"<tr><td style=\"color:#666;vertical-align:top\">{Enc(row.Label)}</td><td><strong>{row.Html}</strong></td></tr>");
        }
        html.Append("</table>");
        html.Append($"<p style=\"margin-top:16px\">{changesHtml}</p>");
        html.Append($"<p style=\"margin-top:16px;color:#666\">{Enc(salon)}</p>");
        html.Append("</div>");

        return new SalonEmailComposer.Content(subject, html.ToString(), text.ToString());
    }

    /// <summary>Primer nombre para el saludo ("Ana López" → "Ana").</summary>
    private static string FirstName(string name)
    {
        var oneLine = OneLine(name);
        var space = oneLine.IndexOf(' ');
        return space < 0 ? oneLine : oneLine[..space];
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
