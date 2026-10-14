using VpsPanel.Overview;

namespace VpsPanel.Tests.Overview;

public sealed class StatusIndicatorTests
{
    private static readonly ContainerView Running = new("web", "nginx", "running", "Up", "");
    private static readonly ContainerView Exited = new("job", "busybox", "exited", "Exited (0)", "");

    private static ServerOverview Overview(
        IReadOnlyList<ComposeProjectView>? compose = null,
        IReadOnlyList<ContainerView>? standalone = null,
        IReadOnlyList<TimerView>? timers = null) =>
        new("s1", ServerStatus.Ok, null, false, null, true, compose ?? [], standalone ?? [], timers ?? [], []);

    private static ComposeProjectView Project(int running, int total, bool stopped = false) =>
        new("demo", "/srv/demo", [], null, running, total, stopped, [], []);

    [Fact(DisplayName = "Everything works => green")]
    public void Compute_Should_ReturnOk_When_EverythingRuns()
    {
        // Arrange
        var overview = Overview([Project(2, 2)], [Running],
            [new TimerView("b.timer", "b.service", null, null, "success")]);

        // Act
        var status = StatusIndicator.Compute(overview);

        // Assert
        status.Should().Be(ServerStatus.Ok);
    }

    [Fact(DisplayName = "Not all project containers are running => yellow")]
    public void Compute_Should_ReturnWarning_When_ProjectPartiallyRuns()
    {
        // Arrange
        var overview = Overview([Project(1, 2)]);

        // Act
        var status = StatusIndicator.Compute(overview);

        // Assert
        status.Should().Be(ServerStatus.Warning);
    }

    [Fact(DisplayName = "Compose project is stopped => yellow")]
    public void Compute_Should_ReturnWarning_When_ProjectStopped()
    {
        // Arrange
        var overview = Overview([Project(0, 0, stopped: true)]);

        // Act
        var status = StatusIndicator.Compute(overview);

        // Assert
        status.Should().Be(ServerStatus.Warning);
    }

    [Fact(DisplayName = "Container without compose is not running => yellow")]
    public void Compute_Should_ReturnWarning_When_StandaloneContainerStopped()
    {
        // Arrange
        var overview = Overview(standalone: [Exited]);

        // Act
        var status = StatusIndicator.Compute(overview);

        // Assert
        status.Should().Be(ServerStatus.Warning);
    }

    [Fact(DisplayName = "Last timer run failed => yellow")]
    public void Compute_Should_ReturnWarning_When_TimerFailed()
    {
        // Arrange
        var overview = Overview(timers: [new TimerView("b.timer", "b.service", null, null, "exit-code")]);

        // Act
        var status = StatusIndicator.Compute(overview);

        // Assert
        status.Should().Be(ServerStatus.Warning);
    }

    [Fact(DisplayName = "Overview with a connection error => red")]
    public void Compute_Should_ReturnError_When_ErrorPresent()
    {
        // Arrange
        var overview = ServerOverview.Failed("s1", "Could not connect over SSH: timeout", false);

        // Act
        var status = StatusIndicator.Compute(overview);

        // Assert
        status.Should().Be(ServerStatus.Error);
    }
}