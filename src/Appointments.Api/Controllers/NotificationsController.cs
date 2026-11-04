using Appointments.Api.Configuration;
using Appointments.Api.Security;
using Appointments.Api.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Controllers;

/// <summary>Estado de los avisos al salón. Requiere <c>X-Api-Key</c>.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[RequireApiKey]
public class NotificationsController : ControllerBase
{
    private readonly INotificationUsageStore _usage;
    private readonly NotificationOptions _options;
    private readonly BusinessOptions _business;
    private readonly TimeProvider _time;

    public NotificationsController(INotificationUsageStore usage, IOptions<NotificationOptions> options, IOptions<BusinessOptions> business, TimeProvider time)
    {
        _usage = usage;
        _options = options.Value;
        _business = business.Value;
        _time = time;
    }

    /// <summary>WhatsApp enviados este mes frente al tope mensual.</summary>
    [HttpGet("whatsapp")]
    public async Task<ActionResult<WhatsAppUsageResponse>> GetWhatsAppUsage(CancellationToken cancellationToken)
    {
        var month = WhatsAppDispatcher.MonthKey(_business, _time.GetUtcNow());
        return Ok(new WhatsAppUsageResponse
        {
            Configured = _options.IsWhatsAppConfigured,
            Month = month,
            Sent = await _usage.GetCountAsync(WhatsAppDispatcher.Channel, month, cancellationToken),
            MonthlyLimit = _options.WhatsAppMonthlyLimit,
        });
    }
}

public class WhatsAppUsageResponse
{
    public bool Configured { get; set; }

    /// <summary>yyyy-MM en la zona horaria del negocio.</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>Intentos de envío este mes (incluye los que fallaron).</summary>
    public int Sent { get; set; }

    public int MonthlyLimit { get; set; }
}
