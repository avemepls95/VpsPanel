using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Api;

public sealed class SecurityTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact(DisplayName = "Ping request => application name")]
    public async Task Ping_Should_ReturnAppName_When_Requested()
    {
        // Arrange
        var client = _app.CreateClient();

        // Act
        var response = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/ping");

        // Assert
        response.Should().BeEquivalentTo(new Dictionary<string, string> { ["app"] = "VpsPanel" });
    }

    [Fact(DisplayName = "Foreign Host => 400")]
    public async Task Request_Should_Return400_When_HostForeign()
    {
        // Arrange
        var client = _app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/ping");
        request.Headers.Host = "evil.example.com";

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "POST without X-VpsPanel => 403 with the error text")]
    public async Task Post_Should_Return403_When_HeaderMissing()
    {
        // Arrange
        var client = _app.CreateClient();

        // Act
        var response = await client.PostAsync("/api/ping", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Be("Header X-VpsPanel is missing.");
    }

    [Fact(DisplayName = "POST with X-VpsPanel => header check passed")]
    public async Task Post_Should_PassHeaderCheck_When_HeaderPresent()
    {
        // Arrange
        var client = _app.CreatePanelClient();

        // Act
        var response = await client.PostAsync("/api/ping", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact(DisplayName = "Any response => CSP and X-Frame-Options headers")]
    public async Task Response_Should_ContainFrameProtection_When_Returned()
    {
        // Arrange
        var client = _app.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ping");

        // Assert
        response.Headers.GetValues("Content-Security-Policy").Should()
            .Equal("frame-ancestors 'none'; script-src 'self'; object-src 'none'");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
    }

    [Fact(DisplayName = "settings.json cannot be parsed => settings status contains the error")]
    public async Task SettingsStatus_Should_ReturnError_When_SettingsInvalid()
    {
        // Arrange
        var client = _app.CreateClient();
        File.WriteAllText(Path.Combine(_app.DataDir, "settings.json"), "{ broken");

        // Act
        var status = await client.GetFromJsonAsync<Dictionary<string, string?>>("/api/settings/status");

        // Assert
        status!["error"].Should().StartWith("Could not parse the settings file");
    }
}