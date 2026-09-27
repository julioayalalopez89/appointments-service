using Appointments.Api.Configuration;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Tests;

public class CorsOriginsTests
{
    private static IConfiguration Config(params string[] origins) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(origins.Select((o, i) =>
                new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", o)))
            .Build();

    /// <summary>Evalúa la política como lo hace el middleware de CORS para un Origin dado.</summary>
    private static string? AllowedOriginFor(string[] allowedOrigins, string requestOrigin)
    {
        var policy = new CorsPolicyBuilder(allowedOrigins).AllowAnyHeader().AllowAnyMethod().Build();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Headers.Origin = requestOrigin;
        var service = new CorsService(Options.Create(new CorsOptions()), NullLoggerFactory.Instance);
        var result = service.EvaluatePolicy(context, policy);
        // AllowedOrigin puede venir relleno aunque el origen no esté permitido; lo que manda es IsOriginAllowed.
        return result.IsOriginAllowed ? result.AllowedOrigin : null;
    }

    [Fact]
    public void Development_AllowsViteDevServer()
    {
        var origins = CorsOrigins.Resolve(Config("https://305hairstyle.com"), isDevelopment: true);

        Assert.Equal("http://localhost:5173", AllowedOriginFor(origins, "http://localhost:5173"));
    }

    [Fact]
    public void Production_DoesNotAllowViteDevServer()
    {
        var origins = CorsOrigins.Resolve(Config("https://305hairstyle.com"), isDevelopment: false);

        Assert.Null(AllowedOriginFor(origins, "http://localhost:5173"));
    }

    [Theory]
    [InlineData("https://305hairstyle.com")]
    [InlineData("https://www.305hairstyle.com")]
    public void NothingConfigured_FallsBackToSalonDomains(string origin)
    {
        var origins = CorsOrigins.Resolve(Config(), isDevelopment: false);

        Assert.Equal(origin, AllowedOriginFor(origins, origin));
    }

    [Fact]
    public void UnknownOrigin_IsRejected()
    {
        var origins = CorsOrigins.Resolve(Config(), isDevelopment: false);

        Assert.Null(AllowedOriginFor(origins, "https://evil.example.com"));
    }

    [Fact]
    public void TrailingSlashesBlanksAndDuplicates_AreCleanedUp()
    {
        var origins = CorsOrigins.Resolve(
            Config(" https://305hairstyle.com/ ", "", "https://305hairstyle.com"),
            isDevelopment: false);

        Assert.Equal(new[] { "https://305hairstyle.com" }, origins);
    }

    [Fact]
    public void ShippedAppSettings_AllowSalonDomains()
    {
        // Lee el appsettings.json real del repo para que un cambio accidental se note en CI.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppointmentsService.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir!.FullName, "src", "Appointments.Api", "appsettings.json"))
            .Build();
        var origins = CorsOrigins.Resolve(config, isDevelopment: false);

        Assert.Contains("https://305hairstyle.com", origins);
        Assert.Contains("https://www.305hairstyle.com", origins);
    }
}
