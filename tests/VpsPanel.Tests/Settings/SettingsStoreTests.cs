using VpsPanel.Settings;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "No settings file => empty server list")]
    public void Read_Should_ReturnNoServers_When_FileMissing()
    {
        // Arrange
        var store = new SettingsStore(new SettingsPaths(_dir.Path));

        // Act
        var settings = store.Read();

        // Assert
        settings.Servers.Should().BeEmpty();
    }

    [Fact(DisplayName = "Settings changed => the change is visible in a new store instance")]
    public void Update_Should_PersistChange_When_Called()
    {
        // Arrange
        var paths = new SettingsPaths(_dir.Path);
        var store = new SettingsStore(paths);
        var server = TestServers.Create("Main");

        // Act
        store.Update(s => s.Servers.Add(server));

        // Assert
        new SettingsStore(paths).Read().Servers.Should().BeEquivalentTo(new[] { server });
    }

    [Fact(DisplayName = "Parallel changes => all are saved")]
    public void Update_Should_KeepEveryChange_When_CalledInParallel()
    {
        // Arrange
        var paths = new SettingsPaths(_dir.Path);
        var store = new SettingsStore(paths);

        // Act
        Parallel.For(0, 20, i => store.Update(s => s.Servers.Add(TestServers.Create($"server-{i}"))));

        // Assert
        new SettingsStore(paths).Read().Servers.Select(s => s.Name).Should()
            .BeEquivalentTo(Enumerable.Range(0, 20).Select(i => $"server-{i}"));
    }

    [Fact(DisplayName = "Invalid JSON => parse error with the file path")]
    public void Read_Should_ThrowParseError_When_JsonInvalid()
    {
        // Arrange
        var paths = new SettingsPaths(_dir.Path);
        File.WriteAllText(paths.SettingsFile, "{ broken");
        var store = new SettingsStore(paths);

        // Act
        var act = () => store.Read();

        // Assert
        act.Should().Throw<SettingsParseException>()
            .WithMessage($"Could not parse the settings file {paths.SettingsFile}:*");
    }

    [Fact(DisplayName = "Invalid JSON => the change does not overwrite the file")]
    public void Update_Should_LeaveFileUntouched_When_JsonInvalid()
    {
        // Arrange
        var paths = new SettingsPaths(_dir.Path);
        File.WriteAllText(paths.SettingsFile, "{ broken");
        var store = new SettingsStore(paths);

        // Act
        var act = () => store.Update(s => s.Servers.Clear());

        // Assert
        act.Should().Throw<SettingsParseException>()
            .WithMessage($"Could not parse the settings file {paths.SettingsFile}:*");
        File.ReadAllText(paths.SettingsFile).Should().Be("{ broken");
    }

    [Fact(DisplayName = "Invalid JSON => GetParseError returns the error text")]
    public void GetParseError_Should_ReturnMessage_When_JsonInvalid()
    {
        // Arrange
        var paths = new SettingsPaths(_dir.Path);
        File.WriteAllText(paths.SettingsFile, "{ broken");
        var store = new SettingsStore(paths);

        // Act
        var error = store.GetParseError();

        // Assert
        error.Should().StartWith($"Could not parse the settings file {paths.SettingsFile}:");
    }

    [Fact(DisplayName = "Server not found => 404 error")]
    public void GetServer_Should_Throw404_When_ServerMissing()
    {
        // Arrange
        var settings = new PanelSettings();

        // Act
        var act = () => settings.GetServer("missing");

        // Assert
        act.Should().Throw<PanelException>().WithMessage("Server not found.")
            .Which.StatusCode.Should().Be(404);
    }
}