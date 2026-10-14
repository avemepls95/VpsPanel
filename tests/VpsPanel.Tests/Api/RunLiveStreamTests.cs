using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VpsPanel.Runs;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Api;

public sealed class RunLiveStreamTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly LogCollector _log = new();
    private readonly TestApp _app;
    private readonly TaskCompletionSource<int?> _gate = new();

    public RunLiveStreamTests() =>
        _app = new TestApp(services => services.AddLogging(b => b.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information).AddProvider(_log)));

    public void Dispose()
    {
        _gate.TrySetResult(0);
        _app.Dispose();
    }

    private Run StartWaiting() =>
        _app.Services.GetRequiredService<RunManager>().Start(
            new RunStart("s1", null, "Test", null, null, ""),
            async _ =>
            {
                await _gate.Task;
                return 0;
            });

    [Fact(DisplayName = "Line is added to a running run => the reader waiting for changes receives it")]
    public async Task Read_Should_YieldLine_When_LineAppendedWhileWaiting()
    {
        // Arrange
        var run = StartWaiting();
        await using var events = RunEventStream.Read(run, 0, CancellationToken.None).GetAsyncEnumerator();
        var next = events.MoveNextAsync();
        next.IsCompleted.Should().BeFalse();

        // Act
        run.Append("live");

        // Assert
        (await next.AsTask().WaitAsync(Wait)).Should().BeTrue();
        events.Current.EventType.Should().Be("line");
        events.Current.EventId.Should().Be("1");
        events.Current.Data.Should().Be("live");
    }

    [Fact(DisplayName = "Running run finishes => the reader waiting for changes receives end with the final status")]
    public async Task Read_Should_YieldEnd_When_RunFinishesWhileWaiting()
    {
        // Arrange
        var run = StartWaiting();
        await using var events = RunEventStream.Read(run, 0, CancellationToken.None).GetAsyncEnumerator();
        var first = events.MoveNextAsync();
        run.Append("live");
        await first.AsTask().WaitAsync(Wait);
        var next = events.MoveNextAsync();
        next.IsCompleted.Should().BeFalse();

        // Act
        _gate.SetResult(0);

        // Assert
        (await next.AsTask().WaitAsync(Wait)).Should().BeTrue();
        events.Current.EventType.Should().Be("end");
        events.Current.Data.Should().Contain("\"status\":\"succeeded\"");
        (await events.MoveNextAsync().AsTask().WaitAsync(Wait)).Should().BeFalse();
    }

    [Fact(DisplayName = "Browser closes the connection to a running run => no error is logged")]
    public async Task Stream_Should_NotLogError_When_ClientDisconnects()
    {
        // Arrange
        var client = _app.CreateClient();
        var run = StartWaiting();
        var connecting = client.GetAsync($"/api/runs/{run.Id}/stream", HttpCompletionOption.ResponseHeadersRead);
        run.Append("live");
        var response = await connecting.WaitAsync(Wait);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        await reader.ReadLineAsync().WaitAsync(Wait);

        // Act
        response.Dispose();

        // Assert
        await WaitForRequestFinished().WaitAsync(Wait);
        _log.Entries.Should().NotContain(e => e.StartsWith("Error") || e.StartsWith("Critical"));
    }

    private async Task WaitForRequestFinished()
    {
        while (!_log.Entries.Any(e => e.Contains("Request finished")))
            await Task.Delay(20);
    }

    private sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Sink(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Sink(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}