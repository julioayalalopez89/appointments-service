namespace Appointments.Api.Security;

/// <summary>
/// Configuración de seguridad (sección <c>Security</c>). La clave nunca va en el repo:
/// en Azure se define con la variable de entorno <c>Security__AdminApiKey</c>.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Clave que deben enviar los endpoints de gestión en la cabecera <c>X-Api-Key</c>.</summary>
    public string? AdminApiKey { get; set; }
}
