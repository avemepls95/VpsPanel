using VpsPanel.Runs;

namespace VpsPanel.Tests.Runs;

public sealed class RunManagerTests
{
    private static RunStart Start(string? lockKey = "s1:compose:demo") =>
        new("s1", "compose:demo", "Update · demo", "Main", lockKey, "An action is already running in this project.");

    [Theory(DisplayName = "Work returned an exit code => status by code")]
    [InlineData(0, "Succeeded")]
    [InlineData(2, "Failed")]
    [InlineData(null, "Failed")]
    public async Task Start_Should_SetStatusByExitCode_When_WorkCompletes(int? exitCode, string expected)
    {
        // Arrange
        var manager = new RunManager(TimeProvider.System);

        // Act
        var run = manager.Start(Start(), _ => Task.FromResult(exitCode));

        // Assert
        await run.Completed;
        run.Status.Should().Be(Enum.Parse<RunStatus>(expected));
        run.ExitCode.Should().Be(exitCode);
    }

    [Fact(DisplayName = "Work threw an exception => status is error and a line with the exception text")]
    public async Task Start_Should_Fail_When_WorkThrows()
    {
        // Arrange
        var manager = new RunManager(TimeProvider.System);

        // Act
        var run = manager.Start(Start(), _ => throw new InvalidOperationException("boom"));

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Failed);
        run.ReadAfter(0).Lines.Should().Equal(new RunLine(1, "Error: boom"));
    }

    [Fact(DisplayName = "Run is stopped => status is stopped")]
    public async Task Stop_Should_SetStoppedStatus_When_RunInProgress()
    {
        // Arrange
        var manager = new RunManager(TimeProvider.System);
        var run = manager.Start(Start(), async r =>
        {
            await Task.Delay(Timeout.Infinite, r.Token);
            return 0;
        });

        // Act
        run.Stop();

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Stopped);
    }

    [Fact(DisplayName = "Project is busy with another run => 409 with the lock message")]
    public void Start_Should_Throw409_When_LockBusy()
    {
        // Arrange
        var manager = new RunManager(TimeProvider.System);
        var gate = new TaskCompletionSource<int?>();
        manager.Start(Start(), _ => gate.Task);

        // Act
        var act = () => manager.Start(Start(), _ => Task.FromResult<int?>(0));

        // Assert
        act.Should().Throw<PanelException>().WithMessage("An action is already running in this project.")
            .Which.StatusCode.Should().Be(409);
        gate.SetResult(0);
    }

    [Fact(DisplayName = "Previous project run is finished => a new run is accepted")]
    public async Task Start_Should_AcceptRun_When_PreviousFinished()
    {
        // Arrange
        var manager = new RunManager(TimeProvider.System);
        await manager.Start(Start(), _ => Task.FromResult<int?>(0)).Completed;

        // Act
        var act = () => manager.Start(Start(), _ => Task.FromResult<int?>(0));

        // Assert
        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Unknown run => 404")]
    public void Get_Should_Throw404_When_RunUnknown()
    {
        // Arrange
        var manager = new RunManager(TimeProvider.System);

        // Act
        var act = () => manager.Get("missing");

        // Assert
        act.Should().Throw<PanelException>().WithMessage("Run not found.")
            .Which.StatusCode.Should().Be(404);
    }
}