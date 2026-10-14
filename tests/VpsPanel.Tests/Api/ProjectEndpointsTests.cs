using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VpsPanel.Projects;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Api;

public sealed class ProjectEndpointsTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact(DisplayName = "Manual project added => project in the response and in the settings")]
    public async Task AddManualProject_Should_ReturnProject_When_Valid()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = TestServers.Create("Main");
        _app.Settings.Update(s => s.Servers.Add(server));

        // Act
        var response = await client.PostAsJsonAsync($"/api/servers/{server.Id}/manual-projects",
            new ManualProjectRequest("sync-job", "/srv/sync-job"));

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("folder").GetString().Should().Be("/srv/sync-job");
        _app.Settings.Read().Servers.Single().ManualProjects.Should().ContainSingle(p => p.Name == "sync-job");
    }

    [Fact(DisplayName = "Unknown command is run => 404")]
    public async Task RunCommand_Should_Return404_When_CommandUnknown()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = TestServers.Create("Main");
        _app.Settings.Update(s => s.Servers.Add(server));

        // Act
        var response = await client.PostAsync($"/api/servers/{server.Id}/commands/missing/run", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}