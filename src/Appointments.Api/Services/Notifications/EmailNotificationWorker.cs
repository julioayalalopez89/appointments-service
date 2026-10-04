namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Servicio en segundo plano que vacía <see cref="EmailQueue"/> y envía cada email.
/// Un fallo de envío solo se registra en el log: la cita ya está guardada.
/// </summary>
public sealed class EmailNotificationWorker : BackgroundService
{
    private readonly EmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailNotificationWorker> _logger;

    public EmailNotificationWorker(EmailQueue queue, IServiceScopeFactory scopes, ILogger<EmailNotificationWorker> logger)
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
                    // IEmailSender usa un HttpClient de IHttpClientFactory: se pide uno nuevo por email.
                    using var scope = _scopes.CreateScope();
                    var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    await sender.SendAsync(message, stoppingToken);
                    _logger.LogInformation("Sent notification email '{Subject}'", message.Subject);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Could not send notification email '{Subject}'", message.Subject);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La app se está apagando.
        }
    }
}
