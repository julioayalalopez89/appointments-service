using System.Net.Http.Headers;
using System.Net.Http.Json;
using Appointments.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Envía emails con la API HTTP de Resend (sin SDK):
/// <c>POST https://api.resend.com/emails</c> con <c>Authorization: Bearer &lt;key&gt;</c>.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    public const string Endpoint = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly NotificationOptions _options;

    public ResendEmailSender(HttpClient http, IOptions<NotificationOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                from = message.From,
                to = new[] { message.To },
                subject = message.Subject,
                html = message.Html,
                text = message.Text,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 500) body = body[..500];
            throw new HttpRequestException(
                $"Resend rejected the email ({(int)response.StatusCode} {response.ReasonPhrase}): {body}",
                null,
                response.StatusCode);
        }
    }
}
