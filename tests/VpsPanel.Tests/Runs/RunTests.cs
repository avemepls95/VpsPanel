using VpsPanel.Runs;

namespace VpsPanel.Tests.Runs;

public sealed class RunTests
{
    private static Run CreateRun() =>
        new("r1", new RunStart("s1", null, "Test", null, null, ""), DateTimeOffset.UnixEpoch);

    [Fact(DisplayName = "More lines than the limit => the first line reports the dropped start, numbering continues")]
    public void ReadAfter_Should_ReportDroppedLines_When_LimitExceeded()
    {
        // Arrange
        var run = CreateRun();
        for (var i = 1; i <= Run.MaxLines + 5; i++)
            run.Append($"line {i}");

        // Act
        var slice = run.ReadAfter(0);

        // Assert
        slice.Lines.Should().HaveCount(Run.MaxLines + 1);
        slice.Lines[0].Should().Be(new RunLine(5, "Lines dropped from the start of the output: 5"));
        slice.Lines[1].Should().Be(new RunLine(6, "line 6"));
        slice.Lines[^1].Should().Be(new RunLine(Run.MaxLines + 5, $"line {Run.MaxLines + 5}"));
    }

    [Fact(DisplayName = "Reconnect with the last line number => only new lines")]
    public void ReadAfter_Should_ReturnOnlyNewLines_When_LastSeenGiven()
    {
        // Arrange
        var run = CreateRun();
        run.Append("a");
        run.Append("b");
        run.Append("c");

        // Act
        var slice = run.ReadAfter(2);

        // Assert
        slice.Lines.Should().Equal(new RunLine(3, "c"));
    }

    [Fact(DisplayName = "Reconnect after dropped lines to the retained part => no dropped-start line")]
    public void ReadAfter_Should_SkipDropNotice_When_LastSeenInsideKeptPart()
    {
        // Arrange
        var run = CreateRun();
        for (var i = 1; i <= Run.MaxLines + 5; i++)
            run.Append($"line {i}");

        // Act
        var slice = run.ReadAfter(Run.MaxLines + 3);

        // Assert
        slice.Lines.Should().Equal(
            new RunLine(Run.MaxLines + 4, $"line {Run.MaxLines + 4}"),
            new RunLine(Run.MaxLines + 5, $"line {Run.MaxLines + 5}"));
    }

    [Fact(DisplayName = "Line with \\r at the end => \\r removed")]
    public void Append_Should_TrimCarriageReturn_When_LineEndsWithIt()
    {
        // Arrange
        var run = CreateRun();

        // Act
        run.Append("text\r");

        // Assert
        run.ReadAfter(0).Lines.Should().Equal(new RunLine(1, "text"));
    }

    [Fact(DisplayName = "New line after a read => the change signal fires")]
    public void Append_Should_CompleteChangedTask_When_LineAdded()
    {
        // Arrange
        var run = CreateRun();
        var changed = run.ReadAfter(0).Changed;

        // Act
        run.Append("text");

        // Assert
        changed.IsCompleted.Should().BeTrue();
    }
}