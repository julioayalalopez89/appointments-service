using System.Text;
using System.Threading.Channels;
using Appointments.Api.Configuration;
using Appointments.Api.Models;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Services.Notifications;

// Avisos al salón por WhatsApp con CallMeBot (https://www.callmebot.com), gratis para
// mandarse mensajes a uno mismo. Mismo esquema que los emails: se encola al instante y un
// worker en segundo plano envía. Si CallMeBot falla, la cita ya está guardada y queda el email.

/// <summary>Un mensaje de WhatsApp para el salón.</summary>
public sealed record WhatsAppMessage(string Text);

public interface IWhatsAppSender
{
    Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// <c>GET https://api.callmebot.com/whatsapp.php?phone=+1...&amp;text=...&amp;apikey=...</c>.
/// CallMeBot a veces responde 200 con el error en el cuerpo, así que también se revisa el texto.
/// </summary>
public sealed class CallMeBotWhatsAppSender : IWhatsAppSender
{
    public const string Endpoint = "https://api.callmebot.com/whatsapp.php";

    private readonly HttpClient _http;
    private readonly NotificationOptions _options;

    public CallMeBotWhatsAppSender(HttpClient http, IOptions<NotificationOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public static string BuildUrl(string phone, string apiKey, string text) =>
        $"{Endpoint}?phone={Uri.EscapeDataString(NormalizePhone(phone))}&text={Uri.EscapeDataString(text)}&apikey={Uri.EscapeDataString(apiKey.Trim())}";

    /// <summary>"+1 (786) 566-9938" → "+17865669938". 10 dígitos sin prefijo = EE. UU.</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 10) digits = "1" + digits;
        return "+" + digits;
    }

    public async Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        var url = BuildUrl(_options.WhatsAppPhone!, _options.CallMeBotApiKey!, message.Text);
        using var response = await _http.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        var bodyError = body.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || body.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || body.Contains("not activated", StringComparison.OrdinalIgnoreCase);
        if (!response.IsSuccessStatusCode || bodyError)
        {
            // El cuerpo puede llevar la URL con la API key: no se copia entera al log.
            var snippet = body.Replace(_options.CallMeBotApiKey!, "***");
            if (snippet.Length > 300) snippet = snippet[..300];
            throw new HttpRequestException(
                $"CallMeBot rejected the WhatsApp message ({(int)response.StatusCode} {response.ReasonPhrase}): {snippet}",
                null,
                response.StatusCode);
        }
    }
}

/// <summary>Cola en memoria para los WhatsApp (como <see cref="EmailQueue"/>).</summary>
public sealed class WhatsAppQueue
{
    private readonly Channel<WhatsAppMessage> _channel =
        Channel.CreateBounded<WhatsAppMessage>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public ChannelReader<WhatsAppMessage> Reader => _channel.Reader;

    public bool TryEnqueue(WhatsAppMessage message) => _channel.Writer.TryWrite(message);
}

/// <summary>Envía los WhatsApp de la cola de uno en uno (CallMeBot no admite ráfagas).</summary>
public sealed class WhatsAppNotificationWorker : BackgroundService
{
    /// <summary>Pausa entre mensajes seguidos para no saturar el servicio gratuito.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(3);

    private readonly WhatsAppQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WhatsAppNotificationWorker> _logger;

    public WhatsAppNotificationWorker(WhatsAppQueue queue, IServiceScopeFactory scopes, ILogger<WhatsAppNotificationWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();
                    await sender.SendAsync(message, stoppingToken);
                    _logger.LogInformation("Sent WhatsApp notification");
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Could not send WhatsApp notification");
                }

                await Task.Delay(Spacing, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La app se está apagando.
        }
    }
}

/// <summary>Texto corto del aviso por WhatsApp (español, hora local del negocio).</summary>
public static class WhatsAppComposer
{
    public static string Booked(Appointment a, BusinessOptions business, string adminUrl) =>
        Compose(a, business, adminUrl, "📅 *Nueva cita*", reason: null);

    public static string Cancelled(Appointment a, BusinessOptions business, string adminUrl, string? reason) =>
        Compose(a, business, adminUrl, "❌ *Cita cancelada*", reason);

    private static string Compose(Appointment a, BusinessOptions business, string adminUrl, string title, string? reason)
    {
        var tz = business.GetTimeZoneInfo();
        var start = TimeZoneInfo.ConvertTime(a.StartTime, tz);
        var end = TimeZoneInfo.ConvertTime(a.EndTime, tz);

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine($"👤 {OneLine(a.CustomerName)}");
        sb.AppendLine($"✂️ {OneLine(a.ServiceName)}");
        sb.AppendLine($"🗓️ {SalonEmailComposer.FormatLocalDate(start)}, {SalonEmailComposer.FormatLocalTime(start)} – {SalonEmailComposer.FormatLocalTime(end)}");
        sb.AppendLine($"📞 {OneLine(a.CustomerPhone)}");
        var wa = SalonEmailComposer.WhatsAppLink(a.CustomerPhone);
        if (wa is not null) sb.AppendLine($"💬 {wa}");
        if (!string.IsNullOrWhiteSpace(a.CustomerEmail)) sb.AppendLine($"✉️ {OneLine(a.CustomerEmail)}");
        if (!string.IsNullOrWhiteSpace(a.Notes)) sb.AppendLine($"📝 {Truncate(OneLine(a.Notes), 300)}");
        if (!string.IsNullOrWhiteSpace(reason)) sb.AppendLine($"Motivo: {Truncate(OneLine(reason), 200)}");
        sb.Append($"Agenda: {adminUrl}");
        return sb.ToString();
    }

    private static string OneLine(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>Encola el aviso por WhatsApp al salón. Sin número o sin API key no hace nada.</summary>
public sealed class WhatsAppNotificationService : INotificationService
{
    private readonly NotificationOptions _options;
    private readonly BusinessOptions _business;
    private readonly WhatsAppQueue _queue;
    private readonly ILogger<WhatsAppNotificationService> _logger;

    public WhatsAppNotificationService(
        IOptions<NotificationOptions> options,
        IOptions<BusinessOptions> business,
        WhatsAppQueue queue,
        ILogger<WhatsAppNotificationService> logger)
    {
        _options = options.Value;
        _business = business.Value;
        _queue = queue;
        _logger = logger;
    }

    public void AppointmentBooked(Appointment appointment) =>
        Enqueue(appointment, () => WhatsAppComposer.Booked(appointment, _business, _options.AdminUrl));

    public void AppointmentCancelled(Appointment appointment, string? reason) =>
        Enqueue(appointment, () => WhatsAppComposer.Cancelled(appointment, _business, _options.AdminUrl, reason));

    private void Enqueue(Appointment appointment, Func<string> compose)
    {
        if (!_options.IsWhatsAppConfigured)
        {
            _logger.LogDebug("WhatsApp notification skipped for {AppointmentId}: Notifications__WhatsAppPhone / CallMeBotApiKey not set.", appointment.Id);
            return;
        }

        if (!_queue.TryEnqueue(new WhatsAppMessage(compose())))
        {
            _logger.LogError("WhatsApp queue is full; dropped notification for appointment {AppointmentId}.", appointment.Id);
        }
    }
}

/// <summary>Reparte cada evento a todos los canales (email y WhatsApp). Un canal que falla no frena a los demás.</summary>
public sealed class CompositeNotificationService : INotificationService
{
    private readonly IReadOnlyList<INotificationService> _channels;
    private readonly ILogger<CompositeNotificationService> _logger;

    public CompositeNotificationService(IEnumerable<INotificationService> channels, ILogger<CompositeNotificationService> logger)
    {
        _channels = channels.ToList();
        _logger = logger;
    }

    public void AppointmentBooked(Appointment appointment) => ForEach(c => c.AppointmentBooked(appointment), appointment.Id);

    public void AppointmentCancelled(Appointment appointment, string? reason) => ForEach(c => c.AppointmentCancelled(appointment, reason), appointment.Id);

    private void ForEach(Action<INotificationService> action, Guid appointmentId)
    {
        foreach (var channel in _channels)
        {
            try
            {
                action(channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification channel {Channel} failed for appointment {AppointmentId}", channel.GetType().Name, appointmentId);
            }
        }
    }
}
