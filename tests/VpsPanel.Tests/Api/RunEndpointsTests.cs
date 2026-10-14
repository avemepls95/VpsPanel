using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VpsPanel.Runs;
using VpsPanel.Tests.Infrastructure;

namespace VpsPanel.Tests.Api;

public sealed class RunEndpointsTests : IDisposable
{
    private const string NL = "\n";
    private const string NN = "\n\n";

    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private async Task<Run> FinishedRun(params string[] lines)
    {
        var manager = _app.Services.GetRequiredService<RunManager>();
        var run = manager.Start(new RunStart("s1", null, "Test", null, null, ""), r =>
        {
            foreach (var line in lines)
                r.Append(line);
            return Task.FromResult<int?>(0);
        });
        await run.Completed;
        return run;
    }

    [Fact(DisplayName = "Output stream without Last-Event-ID => all lines and the end event")]
    public async Task Stream_Should_SendLinesAndEnd_When_NoLastEventId()
    {
        // Arrange
        var client = _app.CreateClient();
        var run = await FinishedRun("first", "second");

        // Act
        var body = await client.GetStringAsync($"/api/runs/{run.Id}/stream");

        // Assert
        body.Should().Contain("event: line").And.Contain("data: first").And.Contain("id: 1")
            .And.Contain("data: second").And.Contain("id: 2")
            .And.Contain("event: end").And.Contain("\"status\":\"succeeded\"");
    }

    [Fact(DisplayName = "Output stream with Last-Event-ID => only the lines after it")]
    public async Task Stream_Should_SkipSeenLines_When_LastEventIdGiven()
    {
        // Arrange
        var client = _app.CreateClient();
        var run = await FinishedRun("first", "second");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{run.Id}/stream");
        request.Headers.Add("Last-Event-ID", "1");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("data: first").And.Contain("data: second");
    }

    [Fact(DisplayName = "Output longer than the limit => one line about the dropped start with number 5 and 10000 output lines with unique numbers")]
    public async Task Stream_Should_SendDropNoticeOnce_When_OutputExceedsLimit()
    {
        // Arrange
        var client = _app.CreateClient();
        var run = await FinishedRun(Enumerable.Range(1, Run.MaxLines + 5).Select(i => $"line {i}").ToArray());

        // Act
        var body = await client.GetStringAsync($"/api/runs/{run.Id}/stream");

        // Assert
        var events = ParseEvents(body);
        var lines = events.Where(e => e.Name == "line").ToList();
        lines.Should().HaveCount(Run.MaxLines + 1);
        lines[0].Should().Be(new SseEvent("line", "5", "Lines dropped from the start of the output: 5"));
        lines.Skip(1).Should().Equal(Enumerable.Range(6, Run.MaxLines).Select(i => new SseEvent("line", i.ToString(), $"line {i}")));
        events.Select(e => e.Name).Last().Should().Be("end");
    }

    [Fact(DisplayName = "Reconnect with Last-Event-ID after overflow => no dropped-start line and no repeats, the stream ends with the end event")]
    public async Task Stream_Should_ContinueWithoutRepeats_When_ReconnectedAfterOverflow()
    {
        // Arrange
        var client = _app.CreateClient();
        var run = await FinishedRun(Enumerable.Range(1, Run.MaxLines + 5).Select(i => $"line {i}").ToArray());
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{run.Id}/stream");
        request.Headers.Add("Last-Event-ID", (Run.MaxLines + 3).ToString());

        // Act
        var response = await client.SendAsync(request);

        // Assert
        var events = ParseEvents(await response.Content.ReadAsStringAsync());
        events.Select(e => e.Name).Should().Equal("line", "line", "end");
        events.Take(2).Should().Equal(
            new SseEvent("line", (Run.MaxLines + 4).ToString(), $"line {Run.MaxLines + 4}"),
            new SseEvent("line", (Run.MaxLines + 5).ToString(), $"line {Run.MaxLines + 5}"));
    }

    [Fact(DisplayName = "Run is finished and all lines are received => 204")]
    public async Task Stream_Should_Return204_When_FinishedAndSeen()
    {
        // Arrange
        var client = _app.CreateClient();
        var run = await FinishedRun("first", "second");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/runs/{run.Id}/stream");
        request.Headers.Add("Last-Event-ID", "2");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact(DisplayName = "List of runs => run with its status and last line number")]
    public async Task List_Should_ReturnRuns_When_RunExists()
    {
        // Arrange
        var client = _app.CreateClient();
        var run = await FinishedRun("first");

        // Act
        var runs = await client.GetFromJsonAsync<JsonElement>("/api/runs");

        // Assert
        var item = runs.EnumerateArray().Single();
        item.GetProperty("id").GetString().Should().Be(run.Id);
        item.GetProperty("status").GetString().Should().Be("succeeded");
        item.GetProperty("lastLineId").GetInt64().Should().Be(1);
    }

    [Fact(DisplayName = "Run is stopped => status is stopped")]
    public async Task Stop_Should_StopRun_When_Requested()
    {
        // Arrange
        var client = _app.CreatePanelClient();
        var manager = _app.Services.GetRequiredService<RunManager>();
        var run = manager.Start(new RunStart("s1", null, "Test", null, null, ""), async r =>
        {
            await Task.Delay(Timeout.Infinite, r.Token);
            return 0;
        });

        // Act
        var response = await client.PostAsync($"/api/runs/{run.Id}/stop", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await run.Completed;
        run.Status.Should().Be(RunStatus.Stopped);
    }

    private static List<SseEvent> ParseEvents(string body) =>
        body.Split(NN, StringSplitOptions.RemoveEmptyEntries).Select(block =>
        {
            string? name = null, id = null, data = null;
            foreach (var line in block.Split(NL))
            {
                if (line.StartsWith("event: ")) name = line[7..];
                else if (line.StartsWith("id: ")) id = line[4..];
                else if (line.StartsWith("data: ")) data = line[6..];
            }
            return new SseEvent(name ?? "message", id, data ?? "");
        }).ToList();

    private sealed record SseEvent(string Name, string? Id, string Data);
}