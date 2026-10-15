using VpsPanel.Settings;
using VpsPanel.Ssh;

namespace VpsPanel.Tests.Ssh;

public sealed class SsmArgumentsTests
{
    [Fact(DisplayName = "Profile and region set => aws gets them before the command")]
    public void Arguments_Should_StartWithProfileAndRegion_When_Set()
    {
        // Arrange
        var ssm = new SsmSettings("i-0123456789abcdef0", "work", "eu-central-1");

        // Act
        var arguments = SsmTunnel.Arguments(ssm, 22);

        // Assert
        arguments.Should().Equal(
            "--profile", "work", "--region", "eu-central-1",
            "ssm", "start-session", "--target", "i-0123456789abcdef0",
            "--document-name", "AWS-StartPortForwardingSession",
            "--parameters", "portNumber=22,localPortNumber=0");
    }

    [Fact(DisplayName = "No profile and region => aws uses its defaults")]
    public void Arguments_Should_OmitProfileAndRegion_When_NotSet()
    {
        // Arrange
        var ssm = new SsmSettings("i-0123456789abcdef0", null, null);

        // Act
        var arguments = SsmTunnel.Arguments(ssm, 22);

        // Assert
        arguments.Should().Equal(
            "ssm", "start-session", "--target", "i-0123456789abcdef0",
            "--document-name", "AWS-StartPortForwardingSession",
            "--parameters", "portNumber=22,localPortNumber=0");
    }

    [Fact(DisplayName = "Session terminated => aws gets the profile, the region and the session id")]
    public void TerminateArguments_Should_PassProfileRegionAndSessionId_When_Set()
    {
        // Arrange
        var ssm = new SsmSettings("i-0123456789abcdef0", "work", "eu-central-1");

        // Act
        var arguments = SsmTunnel.TerminateArguments(ssm, "user-0123456789abcdef0");

        // Assert
        arguments.Should().Equal(
            "--profile", "work", "--region", "eu-central-1",
            "ssm", "terminate-session", "--session-id", "user-0123456789abcdef0");
    }

    [Fact(DisplayName = "SSH on a different port => that port is forwarded")]
    public void Arguments_Should_ForwardServerPort_When_PortIsNotDefault()
    {
        // Arrange
        var ssm = new SsmSettings("i-0123456789abcdef0", null, null);

        // Act
        var arguments = SsmTunnel.Arguments(ssm, 2222);

        // Assert
        arguments[^1].Should().Be("portNumber=2222,localPortNumber=0");
    }
}