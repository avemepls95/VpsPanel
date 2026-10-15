using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VpsPanel.Servers;
using VpsPanel.Settings;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Api;

public sealed class ServerEndpointsTests : IDisposable
{
    private readonly IProviderLookup _providers = Substitute.For<IProviderLookup>();
    private readonly TestApp _app;

    public ServerEndpointsTests() => _app = new TestApp(services => services.AddSingleton(_providers));

    public void Dispose() => _app.Dispose();

    private ServerSettings Seed(Action<ServerSettings>? change = null)
    {
        var server = TestServers.Create("Main");
        change?.Invoke(server);
        _app.Settings.Update(s => s.Servers.Add(server));
        return server;
    }

    [Fact(DisplayName = "New server => saved with the detected hosting")]
    public async Task Create_Should_SaveServerWithProvider_When_LookupFindsIt()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        _providers.Find("192.0.2.10", Arg.Any<CancellationToken>()).Returns("Example Hosting");

        // Act
        var response = await client.PostAsJsonAsync("/api/servers",
            new ServerRequest("Main", "192.0.2.10", 22, "root", @"C:\keys\k", null));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new
            {
                Name = "Main",
                Host = "192.0.2.10",
                Port = 22,
                User = "root",
                KeyPath = @"C:\keys\k",
                Provider = new ProviderInfo("Example Hosting", false),
                HostKeyFingerprint = (string?)null
            }
        });
    }

    [Fact(DisplayName = "Hosting entered on creation => saved as entered manually, no ipinfo request")]
    public async Task Create_Should_SaveManualProvider_When_NameGiven()
    {
        // Arrange
        var client = _app.CreatePanelClient();

        // Act
        await client.PostAsJsonAsync("/api/servers",
            new ServerRequest("Main", "192.0.2.10", 22, "root", @"C:\keys\k", "Example Hosting"));

        // Assert
        _app.Settings.Read().Servers.Single().Provider.Should().Be(new ProviderInfo("Example Hosting", true));
        await _providers.DidNotReceiveWithAnyArgs().Find(default!, default);
    }

    [Fact(DisplayName = "Server name is empty => 400 with the error text")]
    public async Task Create_Should_Return400_When_NameEmpty()
    {
        // Arrange
        var client = _app.CreatePanelClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/servers",
            new ServerRequest(" ", "192.0.2.10", 22, "root", @"C:\keys\k", null));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Be("Enter the server name.");
    }

    [Fact(DisplayName = "Address changed => fingerprint reset, hosting detected again")]
    public async Task Update_Should_ResetFingerprintAndDetectProvider_When_HostChanged()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s =>
        {
            s.HostKeyFingerprint = "SHA256:old";
            s.Provider = new ProviderInfo("Old Hosting", false);
        });
        _providers.Find("192.0.2.20", Arg.Any<CancellationToken>()).Returns("New Hosting");

        // Act
        await client.PutAsJsonAsync($"/api/servers/{server.Id}",
            new ServerRequest("Main", "192.0.2.20", 22, "root", server.KeyPath, "Old Hosting"));

        // Assert
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new { Host = "192.0.2.20", HostKeyFingerprint = (string?)null, Provider = new ProviderInfo("New Hosting", false) }
        });
    }

    [Fact(DisplayName = "Only the port changed => fingerprint reset, hosting detected again")]
    public async Task Update_Should_ResetFingerprintAndDetectProvider_When_PortChanged()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s =>
        {
            s.HostKeyFingerprint = "SHA256:old";
            s.Provider = new ProviderInfo("Old Hosting", false);
        });
        _providers.Find(server.Host, Arg.Any<CancellationToken>()).Returns("New Hosting");

        // Act
        await client.PutAsJsonAsync($"/api/servers/{server.Id}",
            new ServerRequest("Main", server.Host, 2222, "root", server.KeyPath, "Old Hosting"));

        // Assert
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new { Port = 2222, HostKeyFingerprint = (string?)null, Provider = new ProviderInfo("New Hosting", false) }
        });
    }

    [Fact(DisplayName = "Address changed, hosting entered manually => hosting unchanged, ipinfo is not queried")]
    public async Task Update_Should_KeepManualProvider_When_HostChanged()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s => s.Provider = new ProviderInfo("My Hosting", true));

        // Act
        await client.PutAsJsonAsync($"/api/servers/{server.Id}",
            new ServerRequest("Main", "192.0.2.20", 22, "root", server.KeyPath, "My Hosting"));

        // Assert
        _app.Settings.Read().Servers.Single().Provider.Should().Be(new ProviderInfo("My Hosting", true));
        await _providers.DidNotReceiveWithAnyArgs().Find(default!, default);
    }

    [Fact(DisplayName = "Hosting name changed => hosting entered manually, fingerprint unchanged")]
    public async Task Update_Should_MarkProviderManual_When_NameEdited()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s =>
        {
            s.HostKeyFingerprint = "SHA256:same";
            s.Provider = new ProviderInfo("Example Hosting", false);
        });

        // Act
        await client.PutAsJsonAsync($"/api/servers/{server.Id}",
            new ServerRequest("Main", server.Host, 22, "root", server.KeyPath, "Custom hosting"));

        // Assert
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new { HostKeyFingerprint = "SHA256:same", Provider = new ProviderInfo("Custom hosting", true) }
        });
    }

    [Fact(DisplayName = "Detect hosting again => the manually entered value is replaced with the detected one")]
    public async Task RefreshProvider_Should_ReplaceManualProvider_When_Called()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s => s.Provider = new ProviderInfo("My Hosting", true));
        _providers.Find(server.Host, Arg.Any<CancellationToken>()).Returns("Example Hosting");

        // Act
        await client.PostAsync($"/api/servers/{server.Id}/provider/refresh", null);

        // Assert
        _app.Settings.Read().Servers.Single().Provider.Should().Be(new ProviderInfo("Example Hosting", false));
    }

    [Fact(DisplayName = "Fingerprint reset => no fingerprint")]
    public async Task ResetHostKey_Should_ClearFingerprint_When_Called()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s => s.HostKeyFingerprint = "SHA256:old");

        // Act
        await client.PostAsync($"/api/servers/{server.Id}/host-key/reset", null);

        // Assert
        _app.Settings.Read().Servers.Single().HostKeyFingerprint.Should().BeNull();
    }

    [Fact(DisplayName = "New server behind SSM => saved without an address, hosting AWS, no ipinfo request")]
    public async Task Create_Should_SaveSsmServerWithAwsProvider_When_SsmGiven()
    {
        // Arrange
        var client = _app.CreatePanelClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/servers",
            new ServerRequest("Aws", null, 22, "root", @"C:\keys\k", null, new SsmRequest(" i-0123456789abcdef0 ", " ", " eu-central-1 ")));

        // Assert
        (await response.Content.ReadFromJsonAsync<ServerView>())!.Ssm
            .Should().Be(new SsmSettings("i-0123456789abcdef0", null, "eu-central-1"));
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new
            {
                Name = "Aws",
                Host = "",
                Port = 22,
                Ssm = new SsmSettings("i-0123456789abcdef0", null, "eu-central-1"),
                Provider = new ProviderInfo("AWS", false)
            }
        });
        await _providers.DidNotReceiveWithAnyArgs().Find(default!, default);
    }

    [Fact(DisplayName = "Server behind SSM without an instance ID => 400 with the error text")]
    public async Task Create_Should_Return400_When_InstanceIdEmpty()
    {
        // Arrange
        var client = _app.CreatePanelClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/servers",
            new ServerRequest("Aws", null, 22, "root", @"C:\keys\k", null, new SsmRequest(" ", null, null)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Be("Enter the instance ID.");
    }

    [Theory(DisplayName = "Instance ID in a wrong format => 400 with the expected format")]
    [InlineData("-095bc0d6cdadd5b87")]
    [InlineData("i-0123")]
    [InlineData("i-0123456789abcdef0 extra")]
    [InlineData("mi-01234567")]
    public async Task Create_Should_Return400_When_InstanceIdHasWrongFormat(string instanceId)
    {
        // Arrange
        var client = _app.CreatePanelClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/servers",
            new ServerRequest("Aws", null, 22, "root", @"C:\keys\k", null, new SsmRequest(instanceId, null, null)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail
            .Should().Be("The instance ID must look like i-0123456789abcdef0.");
    }

    [Theory(DisplayName = "Instance ID of an EC2 instance or a managed node => saved")]
    [InlineData("i-01234567")]
    [InlineData("i-0123456789abcdef0")]
    [InlineData("mi-0123456789abcdef0")]
    public async Task Create_Should_SaveServer_When_InstanceIdHasValidFormat(string instanceId)
    {
        // Arrange
        var client = _app.CreatePanelClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/servers",
            new ServerRequest("Aws", null, 22, "root", @"C:\keys\k", null, new SsmRequest(instanceId, null, null)));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _app.Settings.Read().Servers.Single().Ssm.Should().Be(new SsmSettings(instanceId, null, null));
    }

    [Fact(DisplayName = "Server switched from SSH to SSM => address cleared, fingerprint reset, hosting AWS without ipinfo")]
    public async Task Update_Should_ResetFingerprintAndSetAws_When_SwitchedToSsm()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s =>
        {
            s.HostKeyFingerprint = "SHA256:old";
            s.Provider = new ProviderInfo("Example Hosting", false);
        });

        // Act
        await client.PutAsJsonAsync($"/api/servers/{server.Id}",
            new ServerRequest("Main", "", 22, "root", server.KeyPath, "Example Hosting", new SsmRequest("i-0123456789abcdef0", null, null)));

        // Assert
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new
            {
                Host = "",
                Ssm = new SsmSettings("i-0123456789abcdef0", null, null),
                HostKeyFingerprint = (string?)null,
                Provider = new ProviderInfo("AWS", false)
            }
        });
        await _providers.DidNotReceiveWithAnyArgs().Find(default!, default);
    }

    [Fact(DisplayName = "Instance ID changed => fingerprint reset")]
    public async Task Update_Should_ResetFingerprint_When_InstanceIdChanged()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s =>
        {
            s.Host = "";
            s.Ssm = new SsmSettings("i-0123456789abcdef0", null, null);
            s.HostKeyFingerprint = "SHA256:old";
            s.Provider = new ProviderInfo("AWS", false);
        });

        // Act
        await client.PutAsJsonAsync($"/api/servers/{server.Id}",
            new ServerRequest("Main", "", 22, "root", server.KeyPath, "AWS", new SsmRequest("i-0fedcba9876543210", null, null)));

        // Assert
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new { Ssm = new SsmSettings("i-0fedcba9876543210", null, null), HostKeyFingerprint = (string?)null }
        });
    }

    [Fact(DisplayName = "Only the AWS profile changed => fingerprint unchanged")]
    public async Task Update_Should_KeepFingerprint_When_ProfileChanged()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s =>
        {
            s.Host = "";
            s.Ssm = new SsmSettings("i-0123456789abcdef0", null, null);
            s.HostKeyFingerprint = "SHA256:same";
            s.Provider = new ProviderInfo("AWS", false);
        });

        // Act
        await client.PutAsJsonAsync($"/api/servers/{server.Id}",
            new ServerRequest("Main", "", 22, "root", server.KeyPath, "AWS", new SsmRequest("i-0123456789abcdef0", "work", null)));

        // Assert
        _app.Settings.Read().Servers.Should().BeEquivalentTo(new[]
        {
            new { Ssm = new SsmSettings("i-0123456789abcdef0", "work", null), HostKeyFingerprint = "SHA256:same" }
        });
    }

    [Fact(DisplayName = "Detect hosting again behind SSM => hosting AWS, no ipinfo request")]
    public async Task RefreshProvider_Should_SetAws_When_ServerUsesSsm()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed(s =>
        {
            s.Host = "";
            s.Ssm = new SsmSettings("i-0123456789abcdef0", null, null);
            s.Provider = new ProviderInfo("My Hosting", true);
        });

        // Act
        await client.PostAsync($"/api/servers/{server.Id}/provider/refresh", null);

        // Assert
        _app.Settings.Read().Servers.Single().Provider.Should().Be(new ProviderInfo("AWS", false));
        await _providers.DidNotReceiveWithAnyArgs().Find(default!, default);
    }

    [Fact(DisplayName = "Server deleted => the server is not in the settings")]
    public async Task Delete_Should_RemoveServer_When_Called()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var server = Seed();

        // Act
        var response = await client.DeleteAsync($"/api/servers/{server.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _app.Settings.Read().Servers.Should().BeEmpty();
    }
}