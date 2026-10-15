using System.Text;
using VpsPanel.Ssh;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Ssh;

[Collection("ssh")]
public sealed class SshSessionTests(SshServerFixture fixture) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact(DisplayName = "Command writes to stdout and stderr => the result contains both streams and the exit code")]
    public async Task Exec_Should_ReturnStreamsAndExitCode_When_CommandFinishes()
    {
        // Arrange
        var server = fixture.Server.Server();
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        var result = await session.Exec("echo out; echo err >&2; exit 3", CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new ExecResult(3, "out\n", "err\n"));
    }

    [Fact(DisplayName = "Command prints lines => lines arrive in order")]
    public async Task ExecStreaming_Should_DeliverLinesInOrder_When_CommandPrintsLines()
    {
        // Arrange
        var server = fixture.Server.Server();
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);
        await using var session = await connector.Connect(server, CancellationToken.None);
        var lines = new List<string>();

        // Act
        var exitCode = await session.ExecStreaming("for i in 1 2 3; do echo line$i; sleep 0.1; done",
            line =>
            {
                lines.Add(line);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        lines.Should().Equal("line1", "line2", "line3");
    }

    [Theory(DisplayName = "Value with special characters quoted with Q => the shell receives the value unchanged")]
    [InlineData("it's")]
    [InlineData("$HOME")]
    [InlineData("a\nb")]
    [InlineData("`id`")]
    [InlineData("-n")]
    [InlineData("")]
    public async Task Exec_Should_PassValueUnchanged_When_ValueQuoted(string value)
    {
        // Arrange
        var server = fixture.Server.Server();
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        var result = await session.Exec($"printf %s {ShellQuote.Q(value)}", CancellationToken.None);

        // Assert
        result.Stdout.Should().Be(value);
    }

    [Fact(DisplayName = "Command writes to stdout and fails => stdout in the stream, stderr and the code in the result")]
    public async Task ExecToStream_Should_CopyStdout_When_CommandFails()
    {
        // Arrange
        var server = fixture.Server.Server();
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);
        await using var session = await connector.Connect(server, CancellationToken.None);
        using var stdout = new MemoryStream();

        // Act
        var result = await session.ExecToStream("printf abc; echo err >&2; exit 2", stdout, CancellationToken.None);

        // Assert
        Encoding.UTF8.GetString(stdout.ToArray()).Should().Be("abc");
        result.Should().BeEquivalentTo(new ExecResult(2, "", "err\n"));
    }

    [Fact(DisplayName = "Write over a file with permissions 600 => new content, same permissions")]
    public async Task OpenWrite_Should_KeepMode_When_OverwritingExistingFile()
    {
        // Arrange
        await fixture.Server.Run("printf old-content > /tmp/mode-test && chmod 600 /tmp/mode-test");
        var server = fixture.Server.Server();
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);
        await using var session = await connector.Connect(server, CancellationToken.None);

        // Act
        await using (var stream = await session.OpenWrite("/tmp/mode-test", createNew: false, CancellationToken.None))
            await stream.WriteAsync("new"u8.ToArray());

        // Assert
        (await fixture.Server.Run("cat /tmp/mode-test")).Should().Be("new");
        (await fixture.Server.Run("stat -c %a /tmp/mode-test")).Should().Be("600\n");
    }

    [Fact(DisplayName = "File on the server => read returns its bytes")]
    public async Task OpenRead_Should_ReturnFileBytes_When_FileExists()
    {
        // Arrange
        await fixture.Server.Run("printf 'hello\\nworld' > /tmp/read-test");
        var server = fixture.Server.Server();
        var connector = new SshConnector(TestSettings.Store(_dir, server), SsmOptions.Default);
        await using var session = await connector.Connect(server, CancellationToken.None);
        using var copy = new MemoryStream();

        // Act
        await using (var stream = await session.OpenRead("/tmp/read-test", CancellationToken.None))
            await stream.CopyToAsync(copy);

        // Assert
        Encoding.UTF8.GetString(copy.ToArray()).Should().Be("hello\nworld");
    }
}