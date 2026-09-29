using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Security;

/// <summary>
/// Protege un endpoint de gestión: exige la cabecera <c>X-Api-Key</c> con el valor de
/// <c>Security:AdminApiKey</c>.
/// <list type="bullet">
/// <item>Clave no configurada en el servidor → <c>503</c> (fallo seguro: nunca queda abierto).</item>
/// <item>Cabecera ausente o incorrecta → <c>401</c>.</item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<SecurityOptions>>().Value;

        if (string.IsNullOrWhiteSpace(options.AdminApiKey))
        {
            context.Result = new ObjectResult(new { message = "Management endpoints are disabled: Security:AdminApiKey is not configured." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
            };
            return;
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(provided) || !KeysMatch(provided, options.AdminApiKey))
        {
            context.Result = new UnauthorizedObjectResult(new { message = $"Missing or invalid {HeaderName} header." });
        }
    }

    // Comparación en tiempo constante para no filtrar la clave por tiempos de respuesta.
    private static bool KeysMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
