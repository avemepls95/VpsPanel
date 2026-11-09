namespace VpsPanel.Containers;

/// <summary>
/// An action of "docker" and "docker compose" that keeps the containers on the server.
/// </summary>
internal enum DockerAction
{
    Restart,
    Stop,
    Start
}

internal static class DockerActions
{
    /// <summary>
    /// Route constraint for the action segment of the URL.
    /// </summary>
    public const string RouteConstraint = "regex(^(restart|stop|start)$)";

    public static DockerAction Parse(string action) => Enum.Parse<DockerAction>(action, ignoreCase: true);

    public static string Command(this DockerAction action) => action.ToString().ToLowerInvariant();
}