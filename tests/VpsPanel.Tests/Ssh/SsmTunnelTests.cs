using System.Diagnostics;
using System.Net.Sockets;
using VpsPanel.Settings;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Ssh;

[Collection("ssm")]
public sealed class SsmTunnelTests : IDisposable
{
    private static readonly SsmSettings Ssm = new("i-0123456789abcdef0", null, null);

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "aws reports the opened port => the tunnel forwards to the SSH server")]
    public async Task Start_Should_ForwardLocalPort_When_SessionStarts()
    {
        // Arrange
        FakeAws.UseMode("ok");

        // Act
        await using var tunnel = await SsmTunnel.Start(Ssm, 22, FakeAws.Options(), CancellationToken.None);

        // Assert
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", tunnel.LocalPort);
        using var reader = new StreamReader(client.GetStream());
        (await reader.ReadLineAsync()).Should().StartWith("SSH-2.0-");
    }

    [Fact(DisplayName = "Tunnel disposed => the local port is closed and aws is stopped")]
    public async Task DisposeAsync_Should_StopProcess_When_Called()
    {
        // Arrange
        FakeAws.UseMode("ok");
        var tunnel = await SsmTunnel.Start(Ssm, 22, FakeAws.Options(), CancellationToken.None);
        var pid = FakeAws.LastPid();
        var port = tunnel.LocalPort;

        // Act
        await tunnel.DisposeAsync();

        // Assert
        FakeAws.IsRunning(pid).Should().BeFalse();
        using var client = new TcpClient();
        var connect = () => client.ConnectAsync("127.0.0.1", port);
        await connect.Should().ThrowAsync<SocketException>();
    }

    [Fact(DisplayName = "Tunnel disposed => the session is terminated in AWS")]
    public async Task DisposeAsync_Should_TerminateSession_When_Called()
    {
        // Arrange
        FakeAws.UseMode("ok");
        File.Delete(FakeAws.TerminatedFile);
        var tunnel = await SsmTunnel.Start(Ssm, 22, FakeAws.Options(), CancellationToken.None);

        // Act
        await tunnel.DisposeAsync();

        // Assert
        (await FakeAws.WaitForTerminatedSessions(TimeSpan.FromSeconds(10))).Should().Contain("fake-0123456789abcdef0\n");
    }

    [Fact(DisplayName = "aws exited by itself after the start => dispose completes")]
    public async Task DisposeAsync_Should_Complete_When_ProcessAlreadyExited()
    {
        // Arrange
        FakeAws.UseMode("ok");
        var tunnel = await SsmTunnel.Start(Ssm, 22, FakeAws.Options(), CancellationToken.None);
        using (var process = Process.GetProcessById(FakeAws.LastPid()))
        {
            process.Kill();
            await process.WaitForExitAsync();
        }

        // Act
        var act = async () => await tunnel.DisposeAsync();

        // Assert
        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "No aws at the path => error about installing AWS CLI")]
    public async Task Start_Should_Throw_When_AwsCliMissing()
    {
        // Arrange
        var options = FakeAws.Options() with { AwsCliPath = Path.Combine(_dir.Path, "missing", "aws.exe") };

        // Act
        var act = () => SsmTunnel.Start(Ssm, 22, options, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<SsmUnavailableException>())
            .WithMessage("Could not start the AWS SSM session: AWS CLI was not found. Install AWS CLI v2 and check that \"aws --version\" works in a terminal.")
            .Which.StatusCode.Should().Be(502);
    }

    [Fact(DisplayName = "No Session Manager plugin => error about installing the plugin")]
    public async Task Start_Should_Throw_When_PluginMissing()
    {
        // Arrange
        FakeAws.UseMode("no-plugin");

        // Act
        var act = () => SsmTunnel.Start(Ssm, 22, FakeAws.Options(), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<SsmUnavailableException>())
            .WithMessage("Could not start the AWS SSM session: the Session Manager plugin for AWS CLI is not installed. See https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html");
    }

    [Fact(DisplayName = "aws fails to start the session => error with the aws message")]
    public async Task Start_Should_ThrowAwsError_When_TargetNotConnected()
    {
        // Arrange
        FakeAws.UseMode("not-connected");

        // Act
        var act = () => SsmTunnel.Start(Ssm, 22, FakeAws.Options(), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<SsmUnavailableException>())
            .WithMessage("Could not start the AWS SSM session: An error occurred (TargetNotConnected) when calling the StartSession operation: i-0123456789abcdef0 is not connected.");
    }

    [Fact(DisplayName = "Session does not start in time => timeout error, aws is stopped")]
    public async Task Start_Should_ThrowTimeout_When_SessionDoesNotStartInTime()
    {
        // Arrange
        FakeAws.UseMode("hang");

        // Act
        var act = () => SsmTunnel.Start(Ssm, 22, FakeAws.Options(TimeSpan.FromSeconds(2)), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<SsmUnavailableException>())
            .WithMessage("Could not start the AWS SSM session: the session did not start within 2 s.");
        FakeAws.IsRunning(FakeAws.LastPid()).Should().BeFalse();
    }

    [Fact(DisplayName = "Request cancelled while the session starts => cancellation, aws is stopped")]
    public async Task Start_Should_StopProcess_When_Cancelled()
    {
        // Arrange
        FakeAws.UseMode("hang");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var act = () => SsmTunnel.Start(Ssm, 22, FakeAws.Options(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        FakeAws.IsRunning(FakeAws.LastPid()).Should().BeFalse();
    }
}