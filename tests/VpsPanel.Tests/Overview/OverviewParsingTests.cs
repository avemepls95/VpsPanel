using VpsPanel.Overview;
using VpsPanel.Settings;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Overview;

public sealed class OverviewParsingTests
{
    private const string Output = """
        @@hostname
        srv-1
        @@os
        Debian GNU/Linux 13 (trixie)
        @@uptime
        2937600.52
        @@cpus
        2
        @@mem
        941621248 672137216
        @@disk
        38654705664 3865470566
        @@docker
        available
        {"name":"demo-web-1","image":"nginx:alpine","state":"running","status":"Up 2 hours","ports":"0.0.0.0:8080->80/tcp","project":"demo","workingDir":"/srv/demo","configFiles":"/srv/demo/compose.yml,/srv/demo/compose.prod.yml","environmentFile":"/srv/demo/.env.prod"}
        {"name":"demo-worker-1","image":"busybox:1.37","state":"exited","status":"Exited (0) 1 hour ago","ports":"","project":"demo","workingDir":"/srv/demo","configFiles":"/srv/demo/compose.yml,/srv/demo/compose.prod.yml","environmentFile":"/srv/demo/.env.prod"}
        {"name":"vpn","image":"example/vpn:1.0","state":"running","status":"Up 3 days","ports":"0.0.0.0:8443->443/tcp","project":"","workingDir":"","configFiles":"","environmentFile":""}
        @@timers
        [{"next":1791921600000000,"left":1791921600000000,"last":1791835200000000,"passed":3994979133797,"unit":"backup.timer","activates":"backup.service"},{"next":1791903985198982,"left":0,"last":null,"passed":null,"unit":"systemd-tmpfiles-clean.timer","activates":"systemd-tmpfiles-clean.service"}]
        @@timer-files
        backup.timer
        @@results
        Result=exit-code
        Id=backup.service
        """;

    [Fact(DisplayName = "Script output => system snapshot")]
    public void Build_Should_ReadSystemSnapshot_When_OutputComplete()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var raw = OverviewOutputParser.Parse(Output);

        // Act
        var overview = OverviewBuilder.Build(server, raw);

        // Assert
        overview.System.Should().Be(new SystemSnapshot("srv-1", "Debian GNU/Linux 13 (trixie)", 2937600.52, 2,
            941621248, 672137216, 38654705664, 3865470566));
    }

    [Fact(DisplayName = "Containers with a project label => compose project with files, env file and counters")]
    public void Build_Should_GroupContainersIntoComposeProject_When_LabelsPresent()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var raw = OverviewOutputParser.Parse(Output);

        // Act
        var overview = OverviewBuilder.Build(server, raw);

        // Assert
        overview.ComposeProjects.Should().BeEquivalentTo(new[]
        {
            new ComposeProjectView("demo", "/srv/demo", ["/srv/demo/compose.yml", "/srv/demo/compose.prod.yml"],
                "/srv/demo/.env.prod", 1, 2, false,
                [
                    new ContainerView("demo-web-1", "nginx:alpine", "running", "Up 2 hours", "0.0.0.0:8080->80/tcp"),
                    new ContainerView("demo-worker-1", "busybox:1.37", "exited", "Exited (0) 1 hour ago", "")
                ],
                [])
        });
    }

    [Fact(DisplayName = "Container without a project label => container without compose")]
    public void Build_Should_ListStandaloneContainer_When_ProjectLabelEmpty()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var raw = OverviewOutputParser.Parse(Output);

        // Act
        var overview = OverviewBuilder.Build(server, raw);

        // Assert
        overview.StandaloneContainers.Should().BeEquivalentTo(new[]
        {
            new ContainerView("vpn", "example/vpn:1.0", "running", "Up 3 days", "0.0.0.0:8443->443/tcp")
        });
    }

    [Fact(DisplayName = "systemd timers => only timers from /etc/systemd/system with the service result")]
    public void Build_Should_KeepOnlyOwnTimers_When_ListContainsSystemTimers()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var raw = OverviewOutputParser.Parse(Output);

        // Act
        var overview = OverviewBuilder.Build(server, raw);

        // Assert
        overview.Timers.Should().BeEquivalentTo(new[]
        {
            new TimerView("backup.timer", "backup.service",
                DateTimeOffset.FromUnixTimeMilliseconds(1791921600000), DateTimeOffset.FromUnixTimeMilliseconds(1791835200000),
                "exit-code")
        });
    }

    [Fact(DisplayName = "Project with its own commands and no containers => stopped project from the settings")]
    public void Build_Should_ShowRememberedProjectAsStopped_When_NoContainers()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var command = new ProjectCommand { Id = "c1", Name = "Restart", Script = "./restart.sh" };
        server.ComposeProjects["landing"] = new ComposeProjectSettings
        {
            WorkingDir = "/srv/landing",
            ConfigFiles = ["/srv/landing/compose.yml"],
            Commands = [command]
        };
        var raw = OverviewOutputParser.Parse(Output);

        // Act
        var overview = OverviewBuilder.Build(server, raw);

        // Assert
        overview.ComposeProjects.Should().ContainEquivalentOf(new ComposeProjectView("landing", "/srv/landing",
            ["/srv/landing/compose.yml"], null, 0, 0, true, [], [new CommandView("c1", "Restart", "./restart.sh")]));
    }

    [Fact(DisplayName = "Docker section is empty => Docker not found, no containers")]
    public void Parse_Should_ReportDockerMissing_When_SectionEmpty()
    {
        // Arrange
        var output = "@@hostname\nsrv-1\n@@docker\n@@timers\n@@timer-files\n@@results\n";

        // Act
        var raw = OverviewOutputParser.Parse(output);

        // Assert
        raw.DockerAvailable.Should().BeFalse();
        raw.Containers.Should().BeEmpty();
    }

    [Fact(DisplayName = "systemctl returned no JSON => no timers, no error")]
    public void Parse_Should_ReturnNoTimers_When_TimersSectionNotJson()
    {
        // Arrange
        var output = "@@timers\nSystem has not been booted with systemd as init system (PID 1).\n@@timer-files\n";

        // Act
        var raw = OverviewOutputParser.Parse(output);

        // Assert
        raw.Timers.Should().BeEmpty();
    }

    [Fact(DisplayName = "Overview script => the docker ps template is quoted in the command")]
    public void Command_Should_ContainQuotedDockerFormat_When_Built()
    {
        // Arrange
        var format = OverviewScript.DockerFormat;

        // Act
        var command = OverviewScript.Command;

        // Assert
        command.Should().StartWith("bash -c '");
        command.Should().Contain("docker ps -a --format '\\''" + format + "'\\''");
    }

    [Fact(DisplayName = "Script source with CRLF => only LF in the command")]
    public void Command_Should_ContainNoCarriageReturn_When_Built()
    {
        // Act
        var command = OverviewScript.Command;

        // Assert
        command.Should().NotContain("\r");
    }
}