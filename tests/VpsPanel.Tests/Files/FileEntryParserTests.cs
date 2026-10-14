using VpsPanel.Files;

namespace VpsPanel.Tests.Files;

public sealed class FileEntryParserTests
{
    [Fact(DisplayName = "find output with unusual names => entries with correct fields, folders first")]
    public void ParseListing_Should_ReadEntries_When_NamesUnusual()
    {
        // Arrange
        const string output =
            "f\0f\0644\012\01791817585.5000000000\0-it's $x\tname\0\0" +
            "d\0d\0755\04096\01791817585.0000000000\0sub\0\0" +
            "l\0d\0777\03\01791817585.0000000000\0to-sub\0sub\0" +
            "f\0f\0600\01\01791817585.0000000000\0a\nb\0\0" +
            "p\0p\0644\00\01791817585.0000000000\0pipe\0\0";

        // Act
        var entries = FileEntryParser.ParseListing(output);

        // Assert
        var modified = DateTimeOffset.FromUnixTimeMilliseconds(1791817585000);
        entries.Should().Equal(
            new FileEntryView("sub", FileEntryType.Dir, null, null, 4096, modified, "755", "rwxr-xr-x"),
            new FileEntryView("to-sub", FileEntryType.Link, FileEntryType.Dir, "sub", 3, modified, "777", "rwxrwxrwx"),
            new FileEntryView("-it's $x\tname", FileEntryType.File, null, null, 12, modified.AddMilliseconds(500), "644", "rw-r--r--"),
            new FileEntryView("a\nb", FileEntryType.File, null, null, 1, modified, "600", "rw-------"),
            new FileEntryView("pipe", FileEntryType.Other, null, null, 0, modified, "644", "rw-r--r--"));
    }

    [Fact(DisplayName = "Empty folder => empty list")]
    public void ParseListing_Should_ReturnEmpty_When_OutputEmpty()
    {
        // Arrange
        const string output = "";

        // Act
        var entries = FileEntryParser.ParseListing(output);

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact(DisplayName = "Folder traversal output => root with an empty relative path and nested objects")]
    public void ParseWalk_Should_ReadEntries_When_TreeGiven()
    {
        // Arrange
        const string output = "d\0755\0\0\0f\04755\0bin/tool\0\0l\0777\0link\0../target\0";

        // Act
        var entries = FileEntryParser.ParseWalk(output);

        // Assert
        entries.Should().Equal(
            new WalkEntry('d', "755", "", ""),
            new WalkEntry('f', "4755", "bin/tool", ""),
            new WalkEntry('l', "777", "link", "../target"));
    }

    [Theory(DisplayName = "Octal permissions => rwx string")]
    [InlineData("644", "rw-r--r--")]
    [InlineData("4755", "rwxr-xr-x")]
    [InlineData("0", "---------")]
    public void Permissions_Should_FormatRwx_When_ModeGiven(string mode, string expected)
    {
        // Arrange
        var input = mode;

        // Act
        var permissions = FileEntryParser.Permissions(input);

        // Assert
        permissions.Should().Be(expected);
    }
}