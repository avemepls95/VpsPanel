using VpsPanel.Files;

namespace VpsPanel.Tests.Files;

public sealed class RemotePathTests
{
    [Theory(DisplayName = "Path with extra parts => normalized path")]
    [InlineData("/", "/")]
    [InlineData("/root/app/", "/root/app")]
    [InlineData("//root//app", "/root/app")]
    [InlineData("/root/./app/../data", "/root/data")]
    [InlineData("/../..", "/")]
    public void Normalize_Should_CollapseParts_When_PathHasExtraParts(string path, string expected)
    {
        // Arrange
        var input = path;

        // Act
        var normalized = RemotePath.Normalize(input);

        // Assert
        normalized.Should().Be(expected);
    }

    [Fact(DisplayName = "Relative path => 400 error")]
    public void Normalize_Should_Throw_When_PathRelative()
    {
        // Arrange
        var path = "root/app";

        // Act
        var act = () => RemotePath.Normalize(path);

        // Assert
        act.Should().Throw<PanelException>().WithMessage("The path must start with /: root/app")
            .Which.StatusCode.Should().Be(400);
    }

    [Theory(DisplayName = "Normalized path => last part")]
    [InlineData("/", "")]
    [InlineData("/root", "root")]
    [InlineData("/root/my file", "my file")]
    public void Name_Should_ReturnLastSegment_When_Called(string path, string expected)
    {
        // Arrange
        var input = path;

        // Act
        var name = RemotePath.Name(input);

        // Assert
        name.Should().Be(expected);
    }

    [Theory(DisplayName = "Normalized path => parent folder")]
    [InlineData("/", "/")]
    [InlineData("/root", "/")]
    [InlineData("/root/app", "/root")]
    public void Parent_Should_ReturnParent_When_Called(string path, string expected)
    {
        // Arrange
        var input = path;

        // Act
        var parent = RemotePath.Parent(input);

        // Assert
        parent.Should().Be(expected);
    }

    [Theory(DisplayName = "Folder and name => full path")]
    [InlineData("/", "etc", "/etc")]
    [InlineData("/root", "app", "/root/app")]
    public void Combine_Should_JoinWithSlash_When_Called(string directory, string name, string expected)
    {
        // Arrange
        var dir = directory;

        // Act
        var combined = RemotePath.Combine(dir, name);

        // Assert
        combined.Should().Be(expected);
    }

    [Theory(DisplayName = "Path and root => nesting flag")]
    [InlineData("/root/app", "/root/app", true)]
    [InlineData("/root/app/data", "/root/app", true)]
    [InlineData("/root/application", "/root/app", false)]
    [InlineData("/etc", "/", true)]
    public void IsInside_Should_DetectNesting_When_Called(string candidate, string root, bool expected)
    {
        // Arrange
        var path = candidate;

        // Act
        var inside = RemotePath.IsInside(path, root);

        // Assert
        inside.Should().Be(expected);
    }

    [Theory(DisplayName = "Invalid file name => 400 error")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    public void ValidateFileName_Should_Throw_When_NameInvalid(string name)
    {
        // Arrange
        var input = name;

        // Act
        var act = () => RemotePath.ValidateFileName(input);

        // Assert
        act.Should().Throw<PanelException>().WithMessage("Invalid file name:*")
            .Which.StatusCode.Should().Be(400);
    }

    [Fact(DisplayName = "Name with a space, quote and leading hyphen => no error")]
    public void ValidateFileName_Should_Accept_When_NameUnusual()
    {
        // Arrange
        var name = "-it's $x.env";

        // Act
        var act = () => RemotePath.ValidateFileName(name);

        // Assert
        act.Should().NotThrow();
    }
}