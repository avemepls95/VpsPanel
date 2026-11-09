using VpsPanel.Containers;
using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Containers;

[Collection("ssh")]
public sealed class ContainerServiceTests(SshServerFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static ContainerService CreateService(SettingsStore store) =>
        new(store, new SshConnector(store, SsmOptions.Default), new RunManager(TimeProvider.System));

    [Theory(DisplayName = "Action on a container => docker runs it with the container name")]
    [InlineData("Restart", "Restart · vpn", "docker restart vpn")]
    [InlineData("Stop", "Stop · vpn", "docker stop vpn")]
    [InlineData("Start", "Start · vpn", "docker start vpn")]
    public async Task Run_Should_RunDockerCommand_When_Called(string action, string title, string output)
    {
        // Arrange
        var server = fixture.Server.Server();
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var run = service.Run(server.Id, "vpn", Enum.Parse<DockerAction>(action));

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        run.Title.Should().Be(title);
        run.ProjectKey.Should().Be("container:vpn");
        run.ReadAfter(0).Lines.Select(l => l.Text).Should().Equal(output);
    }

    [Fact(DisplayName = "docker fails => run fails with the docker message")]
    public async Task Run_Should_Fail_When_DockerFails()
    {
        // Arrange
        var server = fixture.Server.Server();
        var service = CreateService(TestSettings.Store(_dir, server));

        // Act
        var run = service.Run(server.Id, "missing", DockerAction.Stop);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Failed);
        run.ReadAfter(0).Lines.Select(l => l.Text).Should().Equal("Error response from daemon: No such container: missing");
    }
}