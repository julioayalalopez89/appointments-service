using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Appointments.Api.Configuration;
using Appointments.Api.Data;
using Appointments.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Services.Notifications;

// Avisos al salón por WhatsApp con la API oficial de Meta (WhatsApp Cloud API).
//
// - Meta solo deja escribir primero con PLANTILLAS aprobadas (categoría Utility), y cobra cada una
//   entregada. Por eso hay un tope mensual (Notifications:WhatsAppMonthlyLimit) guardado en la base
//   de datos: al llegar, no se envían más WhatsApp hasta el mes siguiente; los emails siguen.
// - Mismo esquema que los emails: se encola al instante y un worker en segundo plano envía.
//   Si Meta falla, la cita ya está guardada.

/// <summary>Un WhatsApp de plantilla para el salón.</summary>
public sealed record WhatsAppMessage(string Template, IReadOnlyList<string> Parameters);

public interface IWhatsAppSender
{
    Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// <c>POST https://graph.facebook.com/{version}/{phone-number-id}/messages</c> con
/// <c>Authorization: Bearer &lt;token&gt;</c> y un mensaje de tipo <c>template</c>.
/// </summary>
public sealed class MetaWhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _http;
    private readonly NotificationOptions _options;

    public MetaWhatsAppSender(HttpClient http, IOptions<NotificationOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>"+1 (786) 566-9938" → "17865669938" (Meta quiere solo dígitos). 10 dígitos = EE. UU.</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 10 ? "1" + digits : digits;
    }

    public static object BuildPayload(string to, string language, WhatsAppMessage message) => new
    {
        messaging_product = "whatsapp",
        to = NormalizePhone(to),
        type = "template",
        template = new
        {
            name = message.Template,
            language = new { code = language },
            components = new object[]
            {
                new
                {
                    type = "body",
                    parameters = message.Parameters.Select(p => new { type = "text", text = p }).ToArray(),
                },
            },
        },
    };

    public async Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        var url = $"https://graph.facebook.com/{_options.WhatsAppGraphVersion}/{Uri.EscapeDataString(_options.WhatsAppPhoneNumberId!.Trim())}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(BuildPayload(_options.WhatsAppTo!, _options.WhatsAppTemplateLanguage, message)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.WhatsAppAccessToken!.Trim());

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 500) body = body[..500];
            throw new HttpRequestException(
                $"Meta rejected the WhatsApp message ({(int)response.StatusCode} {response.ReasonPhrase}): {body}",
                null,
                response.StatusCode);
        }
    }
}

/// <summary>Cuenta de mensajes por canal y mes ("whatsapp", "2026-11").</summary>
public class NotificationUsage
{
    public string Channel { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public int Count { get; set; }
}

public interface INotificationUsageStore
{
    /// <summary>Suma 1 si aún no se llegó a <paramref name="limit"/> este mes. False = tope alcanzado.</summary>
    Task<bool> TryReserveAsync(string channel, string month, int limit, CancellationToken cancellationToken);

    Task<int> GetCountAsync(string channel, string month, CancellationToken cancellationToken);
}

public sealed class EfNotificationUsageStore : INotificationUsageStore
{
    private readonly AppointmentsDbContext _db;

    public EfNotificationUsageStore(AppointmentsDbContext db) => _db = db;

    // Lo llama un solo worker, de uno en uno: no hace falta más control de concurrencia.
    public async Task<bool> TryReserveAsync(string channel, string month, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0) return false;

        var row = await _db.NotificationUsage.FindAsync(new object[] { channel, month }, cancellationToken);
        if (row is null)
        {
            row = new NotificationUsage { Channel = channel, Month = month, Count = 0 };
            _db.NotificationUsage.Add(row);
        }

        if (row.Count >= limit) return false;

        row.Count++;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> GetCountAsync(string channel, string month, CancellationToken cancellationToken) =>
        (await _db.NotificationUsage.FindAsync(new object[] { channel, month }, cancellationToken))?.Count ?? 0;
}

/// <summary>Decide si un WhatsApp se envía (tope mensual) y lo envía. Scoped (usa la base de datos).</summary>
public sealed class WhatsAppDispatcher
{
    public const string Channel = "whatsapp";

    private readonly INotificationUsageStore _usage;
    private readonly IWhatsAppSender _sender;
    private readonly NotificationOptions _options;
    private readonly BusinessOptions _business;
    private readonly TimeProvider _time;
    private readonly ILogger<WhatsAppDispatcher> _logger;

    public WhatsAppDispatcher(
        INotificationUsageStore usage, IWhatsAppSender sender, IOptions<NotificationOptions> options,
        IOptions<BusinessOptions> business, TimeProvider time, ILogger<WhatsAppDispatcher> logger)
    {
        _usage = usage;
        _sender = sender;
        _options = options.Value;
        _business = business.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>Mes actual en la zona del negocio, "yyyy-MM" (el tope se reinicia el día 1).</summary>
    public static string MonthKey(BusinessOptions business, DateTimeOffset now) =>
        TimeZoneInfo.ConvertTime(now, business.GetTimeZoneInfo()).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True si se envió; false si se omitió por el tope mensual.</summary>
    public async Task<bool> DispatchAsync(WhatsAppMessage message, CancellationToken cancellationToken)
    {
        var month = MonthKey(_business, _time.GetUtcNow());
        var limit = _options.WhatsAppMonthlyLimit;

        // Se cuenta antes de enviar: si el envío falla, el intento cuenta igual (nunca se pasa del tope).
        if (!await _usage.TryReserveAsync(Channel, month, limit, cancellationToken))
        {
            _logger.LogWarning(
                "WhatsApp monthly limit reached ({Limit} in {Month}); notification skipped (email still sent). " +
                "Raise Notifications__WhatsAppMonthlyLimit to send more.", limit, month);
            return false;
        }

        await _sender.SendAsync(message, cancellationToken);
        return true;
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

/// <summary>Vacía <see cref="WhatsAppQueue"/> de uno en uno. Un fallo solo se registra en el log.</summary>
public sealed class WhatsAppNotificationWorker : BackgroundService
{
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
                    var dispatcher = scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>();
                    if (await dispatcher.DispatchAsync(message, stoppingToken))
                    {
                        _logger.LogInformation("Sent WhatsApp notification ({Template})", message.Template);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Could not send WhatsApp notification ({Template})", message.Template);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La app se está apagando.
        }
    }
}

/// <summary>Variables de las plantillas: {{1}} clienta, {{2}} servicio, {{3}} fecha y hora, {{4}} teléfono.</summary>
public static class WhatsAppComposer
{
    /// <summary>Meta rechaza variables con saltos de línea, tabuladores o más de 4 espacios seguidos.</summary>
    public static string Clean(string? s, int max = 200)
    {
        var one = string.Join(' ', (s ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (one.Length == 0) one = "-";
        return one.Length <= max ? one : one[..(max - 1)] + "…";
    }

    public static IReadOnlyList<string> Parameters(Appointment a, BusinessOptions business)
    {
        var tz = business.GetTimeZoneInfo();
        var start = TimeZoneInfo.ConvertTime(a.StartTime, tz);
        var end = TimeZoneInfo.ConvertTime(a.EndTime, tz);
        var when = $"{SalonEmailComposer.FormatLocalDate(start)}, {SalonEmailComposer.FormatLocalTime(start)} – {SalonEmailComposer.FormatLocalTime(end)}";
        return new[] { Clean(a.CustomerName), Clean(a.ServiceName), Clean(when), Clean(a.CustomerPhone, 40) };
    }
}

/// <summary>Encola el WhatsApp al salón. Sin configuración no hace nada.</summary>
public sealed class WhatsAppNotificationService : INotificationService
{
    private readonly NotificationOptions _options;
    private readonly BusinessOptions _business;
    private readonly WhatsAppQueue _queue;
    private readonly ILogger<WhatsAppNotificationService> _logger;

    public WhatsAppNotificationService(
        IOptions<NotificationOptions> options, IOptions<BusinessOptions> business,
        WhatsAppQueue queue, ILogger<WhatsAppNotificationService> logger)
    {
        _options = options.Value;
        _business = business.Value;
        _queue = queue;
        _logger = logger;
    }

    public void AppointmentBooked(Appointment appointment) => Enqueue(appointment, _options.WhatsAppBookedTemplate);

    public void AppointmentCancelled(Appointment appointment, string? reason) => Enqueue(appointment, _options.WhatsAppCancelledTemplate);

    private void Enqueue(Appointment appointment, string template)
    {
        if (!_options.IsWhatsAppConfigured || _options.WhatsAppMonthlyLimit <= 0)
        {
            return;
        }

        if (!_queue.TryEnqueue(new WhatsAppMessage(template, WhatsAppComposer.Parameters(appointment, _business))))
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
