using System.Threading.Channels;

namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Cola en memoria entre la API (que encola y responde enseguida) y
/// <see cref="EmailNotificationWorker"/> (que envía en segundo plano).
/// Si la app se reinicia, los emails aún en cola se pierden: aceptable para avisos.
/// </summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    /// <summary>Encola el email. Devuelve false si la cola está llena.</summary>
    public bool TryEnqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}
