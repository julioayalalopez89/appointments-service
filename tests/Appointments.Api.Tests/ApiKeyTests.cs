using System.Reflection;
using Appointments.Api.Controllers;
using Appointments.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Appointments.Api.Tests;

public class ApiKeyTests
{
    private const string Key = "test-admin-key";

    private static AuthorizationFilterContext Run(string? configuredKey, string? headerValue)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new SecurityOptions { AdminApiKey = configuredKey }))
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        if (headerValue is not null)
        {
            http.Request.Headers[RequireApiKeyAttribute.HeaderName] = headerValue;
        }

        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

        new RequireApiKeyAttribute().OnAuthorization(context);
        return context;
    }

    [Fact]
    public void CorrectKey_LetsRequestThrough()
    {
        Assert.Null(Run(Key, Key).Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData("test-admin-key ")]
    public void MissingOrWrongKey_Returns401(string? header)
    {
        var result = Run(Key, header).Result;

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void KeyNotConfigured_Returns503_EvenWithAHeader(string? configured)
    {
        var result = Run(configured, "anything").Result;

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
    }

    [Theory]
    [InlineData(nameof(AppointmentsController.GetAll))]
    [InlineData(nameof(AppointmentsController.GetById))]
    [InlineData(nameof(AppointmentsController.Update))]
    [InlineData(nameof(AppointmentsController.Cancel))]
    [InlineData(nameof(AppointmentsController.Delete))]
    public void ManagementEndpoints_RequireApiKey(string action)
    {
        var method = typeof(AppointmentsController).GetMethod(action)!;

        Assert.NotNull(method.GetCustomAttribute<RequireApiKeyAttribute>());
    }

    [Fact]
    public void PublicEndpoints_DoNotRequireApiKey()
    {
        var create = typeof(AppointmentsController).GetMethod(nameof(AppointmentsController.Create))!;

        Assert.Null(create.GetCustomAttribute<RequireApiKeyAttribute>());
        Assert.Null(typeof(AppointmentsController).GetCustomAttribute<RequireApiKeyAttribute>());
        Assert.Null(typeof(AvailabilityController).GetCustomAttribute<RequireApiKeyAttribute>());
        Assert.Empty(typeof(AvailabilityController).GetMethods()
            .Where(m => m.GetCustomAttribute<RequireApiKeyAttribute>() is not null));
    }
}
