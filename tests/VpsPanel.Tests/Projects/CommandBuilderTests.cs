using VpsPanel.Containers;
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

    [Theory(DisplayName = "Action on a compose project => docker compose with the project parameters and the action")]
    [InlineData("Restart", "restart")]
    [InlineData("Stop", "stop")]
    [InlineData("Start", "start")]
    public void ComposeAction_Should_PassProjectParameters_When_Called(string action, string expected)
    {
        // Arrange
        var project = new ComposeProjectView("demo", "/srv/demo", ["/srv/demo/compose.yml"], "/srv/demo/.env", 1, 1, false, [], []);

        // Act
        var command = CommandBuilder.ComposeAction(project, Enum.Parse<DockerAction>(action));

        // Assert
        command.Should().Be($"docker compose -p 'demo' -f '/srv/demo/compose.yml' --env-file '/srv/demo/.env' {expected}");
    }

    [Fact(DisplayName = "Action on a container with a quote in the name => docker with the quoted name and stderr in the output")]
    public void ContainerAction_Should_QuoteName_When_Called()
    {
        // Arrange
        var name = "it's";

        // Act
        var command = CommandBuilder.ContainerAction(name, DockerAction.Restart);

        // Assert
        command.Should().Be("docker restart 'it'\\''s' 2>&1");
    }
}