namespace Appointments.Api.Configuration;

/// <summary>
/// Orígenes (dominios del frontend) que pueden llamar a la API desde el navegador.
/// Se leen de "Cors:AllowedOrigins" (appsettings.json o, en Azure, las variables
/// Cors__AllowedOrigins__0..n).
/// </summary>
public static class CorsOrigins
{
    public const string SectionName = "Cors:AllowedOrigins";

    /// <summary>Por defecto en producción: el sitio del salón, con y sin www.</summary>
    public static readonly string[] ProductionDefaults =
    {
        "https://305hairstyle.com",
        "https://www.305hairstyle.com",
    };

    /// <summary>Servidor de desarrollo de Vite (305hairstyle_web).</summary>
    public const string ViteDevServer = "http://localhost:5173";

    /// <summary>
    /// Devuelve los orígenes permitidos. Quita espacios, entradas vacías,
    /// duplicados y la "/" final (el navegador envía el Origin sin ella, así que
    /// "https://305hairstyle.com/" nunca coincidiría). Si no hay nada
    /// configurado usa <see cref="ProductionDefaults"/>. En desarrollo siempre
    /// añade <see cref="ViteDevServer"/>.
    /// </summary>
    public static string[] Resolve(IConfiguration configuration, bool isDevelopment)
    {
        var configured = (configuration.GetSection(SectionName).Get<string[]>() ?? Array.Empty<string>())
            .Select(o => (o ?? string.Empty).Trim().TrimEnd('/'))
            .Where(o => o.Length > 0);

        var origins = configured.Any() ? configured.ToList() : ProductionDefaults.ToList();

        if (isDevelopment)
        {
            origins.Add(ViteDevServer);
        }

        return origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
