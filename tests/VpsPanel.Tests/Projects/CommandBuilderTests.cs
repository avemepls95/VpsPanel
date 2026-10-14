using VpsPanel.Overview;
using VpsPanel.Projects;

namespace VpsPanel.Tests.Projects;

public sealed class CommandBuilderTests
{
    [Fact(DisplayName = "Folder with a space and a script with quotes => cd that exits on error and an escaped bash -lc")]
    public void InFolder_Should_QuoteFolderAndScript_When_Called()
    {
        // Arrange
        var folder = "/srv/my app";

        // Act
        var command = CommandBuilder.InFolder(folder, "echo 'hi'; pwd");

        // Assert
        command.Should().Be("bash -lc 'cd -- '\\''/srv/my app'\\'' || exit 1\necho '\\''hi'\\''; pwd' 2>&1");
    }

    [Fact(DisplayName = "Project with two files and an env file => pull and up -d with the same parameters")]
    public void ComposeUpdate_Should_PassProjectFilesAndEnv_When_Present()
    {
        // Arrange
        var project = new ComposeProjectView("demo", "/srv/demo", ["/srv/demo/compose.yml", "/srv/demo/compose.prod.yml"],
            "/srv/demo/.env.prod", 2, 2, false, [], []);

        // Act
        var command = CommandBuilder.ComposeUpdate(project);

        // Assert
        const string args = "-p 'demo' -f '/srv/demo/compose.yml' -f '/srv/demo/compose.prod.yml' --env-file '/srv/demo/.env.prod'";
        command.Should().Be($"docker compose {args} pull && docker compose {args} up -d");
    }

    [Fact(DisplayName = "Project without an env file => no --env-file")]
    public void ComposeUpdate_Should_OmitEnvFile_When_Absent()
    {
        // Arrange
        var project = new ComposeProjectView("demo", "/srv/demo", ["/srv/demo/compose.yml"], null, 1, 1, false, [], []);

        // Act
        var command = CommandBuilder.ComposeUpdate(project);

        // Assert
        command.Should().Be("docker compose -p 'demo' -f '/srv/demo/compose.yml' pull && docker compose -p 'demo' -f '/srv/demo/compose.yml' up -d");
    }
}