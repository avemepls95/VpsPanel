using NSubstitute;
using VpsPanel.Reboot;
using VpsPanel.Runs;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Reboot;

public sealed class RebootServiceTests : IDisposable
{
    private const string BootIdCommand = "cat /proc/sys/kernel/random/boot_id";
    private static readonly RebootTiming FastTiming = new(TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1), 3);

    private readonly TempDirectory _dir = new();
    private readonly ISshConnector _connector = Substitute.For<ISshConnector>();
    private readonly AutoAdvanceTimeProvider _time = new();

    public void Dispose() => _dir.Dispose();

    private RebootService CreateService(ServerSettings server, RebootTiming? timing = null)
    {
        timing ??= FastTiming;
        _time.InstantTimersUpTo = timing.PollInterval;
        return new RebootService(TestSettings.Store(_dir, server), _connector, new RunManager(TimeProvider.System), timing)
        {
            Time = _time
        };
    }

    private static ISshSession SessionWithBootId(string bootId)
    {
        var session = Substitute.For<ISshSession>();
        session.Exec(BootIdCommand, Arg.Any<CancellationToken>()).Returns(new ExecResult(0, bootId + "\n", ""));
        session.Exec("systemctl reboot", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ExecResult>(new IOException("connection closed")));
        return session;
    }

    private static ISshSession SessionDroppingOnBootId()
    {
        var session = Substitute.For<ISshSession>();
        session.Exec(BootIdCommand, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ExecResult>(new IOException("connection reset")));
        return session;
    }

    private static ISshSession SessionHangingOnBootId(TaskCompletionSource execStarted)
    {
        var session = Substitute.For<ISshSession>();
        session.Exec(BootIdCommand, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            execStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return new ExecResult(0, "", "");
        });
        return session;
    }

    private static Task<ISshSession> Unavailable() =>
        Task.FromException<ISshSession>(new SshUnavailableException("the server did not respond within 1 s"));

    private static IEnumerable<string> Lines(Run run) => run.ReadAfter(0).Lines.Select(l => l.Text);

    [Fact(DisplayName = "boot_id differs after the reboot => run succeeds")]
    public async Task Start_Should_Succeed_When_BootIdChanges()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(server);
        var before = SessionWithBootId("boot-a");
        var after = SessionWithBootId("boot-b");
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ => Task.FromResult(before), _ => Unavailable(), _ => Task.FromResult(after));

        // Act
        var run = service.Start(server.Id);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        Lines(run).Should().Equal("Sending the reboot command", "Waiting for the server to respond again", "The server is responding again");
    }

    [Fact(DisplayName = "Server did not return within the allowed attempts => run fails")]
    public async Task Start_Should_Fail_When_ServerDoesNotReturn()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(server);
        var before = SessionWithBootId("boot-a");
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ => Task.FromResult(before), _ => Unavailable());

        // Act
        var run = service.Start(server.Id);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Failed);
        Lines(run).Last().Should().Be("The server did not respond after the reboot");
    }

    [Fact(DisplayName = "Connection attempts wait a long time for a reply => the wait is limited by the total time")]
    public async Task Start_Should_FailByElapsedTime_When_AttemptsAreSlow()
    {
        // Arrange
        var timing = new RebootTiming(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 10);
        var server = TestServers.Create("Main");
        var service = CreateService(server, timing);
        var before = SessionWithBootId("boot-a");
        var calls = 0;
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ =>
            {
                if (calls++ == 0)
                    return Task.FromResult(before);

                _time.Advance(timing.ConnectTimeout);
                return Unavailable();
            });

        // Act
        var run = service.Start(server.Id);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Failed);
        Lines(run).Last().Should().Be("The server did not respond after the reboot");
        calls.Should().Be(1 + 5);
    }

    [Fact(DisplayName = "Server accepted the connection but does not answer the command => run fails when the deadline passes")]
    public async Task Start_Should_FailByDeadline_When_ExecHangs()
    {
        // Arrange
        var timing = new RebootTiming(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 10);
        var server = TestServers.Create("Main");
        var service = CreateService(server, timing);
        var execStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = SessionWithBootId("boot-a");
        var hanging = SessionHangingOnBootId(execStarted);
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ => Task.FromResult(before), _ => Task.FromResult(hanging));
        var run = service.Start(server.Id);
        await execStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Act
        _time.Advance(timing.PollInterval * timing.MaxAttempts);

        // Assert
        await run.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        run.Status.Should().Be(RunStatus.Failed);
        Lines(run).Last().Should().Be("The server did not respond after the reboot");
    }

    [Fact(DisplayName = "User stops the run during polling => run is stopped")]
    public async Task Stop_Should_StopRun_When_PollingInProgress()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(server);
        var execStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = SessionWithBootId("boot-a");
        var hanging = SessionHangingOnBootId(execStarted);
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ => Task.FromResult(before), _ => Task.FromResult(hanging));
        var run = service.Start(server.Id);
        await execStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Act
        run.Stop();

        // Assert
        await run.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        run.Status.Should().Be(RunStatus.Stopped);
    }

    [Fact(DisplayName = "boot_id did not change => waiting continues until a different boot_id")]
    public async Task Start_Should_KeepWaiting_When_BootIdSame()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(server);
        var before = SessionWithBootId("boot-a");
        var stillOld = SessionWithBootId("boot-a");
        var after = SessionWithBootId("boot-b");
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ => Task.FromResult(before), _ => Task.FromResult(stillOld), _ => Task.FromResult(stillOld), _ => Task.FromResult(after));

        // Act
        var run = service.Start(server.Id);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        await _connector.Received(4).Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>());
        Lines(run).Should().Equal("Sending the reboot command", "Waiting for the server to respond again", "The server is responding again");
    }

    [Fact(DisplayName = "Connection dropped during polling => polling continues")]
    public async Task Start_Should_KeepWaiting_When_ConnectionDropsDuringPolling()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(server);
        var before = SessionWithBootId("boot-a");
        var dropping = SessionDroppingOnBootId();
        var after = SessionWithBootId("boot-b");
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ => Task.FromResult(before), _ => Task.FromResult(dropping), _ => Task.FromResult(after));

        // Act
        var run = service.Start(server.Id);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Succeeded);
        Lines(run).Last().Should().Be("The server is responding again");
    }

    [Fact(DisplayName = "Server key changed while waiting => run fails with a key error")]
    public async Task Start_Should_Fail_When_HostKeyChanged()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(server);
        var before = SessionWithBootId("boot-a");
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(_ => Task.FromResult(before),
                _ => Task.FromException<ISshSession>(new HostKeyMismatchException("Main", "SHA256:new")));

        // Act
        var run = service.Start(server.Id);

        // Assert
        await run.Completed;
        run.Status.Should().Be(RunStatus.Failed);
        Lines(run).Last().Should().StartWith("Error: The key for server 'Main' has changed");
    }

    [Fact(DisplayName = "Reboot is already in progress => 409")]
    public void Start_Should_Throw409_When_RebootRunning()
    {
        // Arrange
        var server = TestServers.Create("Main");
        var service = CreateService(server);
        var gate = new TaskCompletionSource<ISshSession>();
        _connector.Connect(Arg.Any<ServerSettings>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>()).Returns(gate.Task);
        var first = service.Start(server.Id);

        // Act
        var act = () => service.Start(server.Id);

        // Assert
        act.Should().Throw<PanelException>().WithMessage("This server is already rebooting.")
            .Which.StatusCode.Should().Be(409);
        first.Stop();
        gate.SetCanceled();
    }
}