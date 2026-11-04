using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Appointments.Api.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Appointments.Api.Tests;

/// <summary>
/// Rate limiting por IP de los endpoints públicos (issue #15). Se usan límites
/// pequeños para no tener que hacer decenas de peticiones.
/// </summary>
public class RateLimitingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static WebApplicationFactoryWithLimits CreateFactory(int bookingsPerHour = 10, int availabilityPerMinute = 60) =>
        new(bookingsPerHour, availabilityPerMinute);

    [Fact]
    public async Task Bookings_OverTheLimit_Return429WithMessageAndRetryAfter()
    {
        using var factory = CreateFactory(bookingsPerHour: 2);
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(10)), Json);
        var second = await client.PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(11)), Json);
        var third = await client.PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(12)), Json);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);

        var body = await third.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("Too many requests", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Availability_OverTheLimit_Returns429()
    {
        using var factory = CreateFactory(availabilityPerMinute: 3);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var ok = await client.GetAsync("/api/availability?date=2026-09-29");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var limited = await client.GetAsync("/api/availability?date=2026-09-29");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Limits_AreSeparatePerEndpoint()
    {
        using var factory = CreateFactory(bookingsPerHour: 1, availabilityPerMinute: 1);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/availability?date=2026-09-29")).StatusCode);
        var booking = await client.PostAsJsonAsync("/api/appointments", TestData.Request(TestData.Tuesday(10)), Json);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
    }

    [Fact]
    public async Task Limits_ArePerClientIp()
    {
        using var factory = CreateFactory(availabilityPerMinute: 1);
        var client = factory.CreateClient();

        Task<HttpResponseMessage> GetFrom(string ip)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/availability?date=2026-09-29");
            request.Headers.Add("X-Forwarded-For", ip);
            return client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, (await GetFrom("203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetFrom("203.0.113.1")).StatusCode);
        // Otra IP tiene su propio contador.
        Assert.Equal(HttpStatusCode.OK, (await GetFrom("203.0.113.2")).StatusCode);
    }

    [Fact]
    public async Task ManagementEndpoints_AreNotRateLimited()
    {
        using var factory = CreateFactory(bookingsPerHour: 1, availabilityPerMinute: 1);
        var admin = factory.CreateAdminClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/appointments")).StatusCode);
        }
    }

    /// <summary><see cref="ApiFactory"/> con límites de rate limiting propios.</summary>
    private sealed class WebApplicationFactoryWithLimits : IDisposable
    {
        private readonly ApiFactory _inner = new();
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;

        public WebApplicationFactoryWithLimits(int bookingsPerHour, int availabilityPerMinute)
        {
            _factory = _inner.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.PostConfigure<RateLimitingOptions>(options =>
                {
                    options.BookingsPerHour = bookingsPerHour;
                    options.AvailabilityPerMinute = availabilityPerMinute;
                })));
        }

        public HttpClient CreateClient() => _factory.CreateClient();

        public HttpClient CreateAdminClient()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add(Appointments.Api.Security.RequireApiKeyAttribute.HeaderName, ApiFactory.AdminKey);
            return client;
        }

        public void Dispose()
        {
            _factory.Dispose();
            _inner.Dispose();
        }
    }
}
