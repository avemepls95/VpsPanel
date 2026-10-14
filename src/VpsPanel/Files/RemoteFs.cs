using System.Globalization;
using VpsPanel.Ssh;
using static VpsPanel.Ssh.ShellQuote;

namespace VpsPanel.Files;

internal static class RemoteFs
{
    private const int ScriptChunk = 200;
    private const string ListFormat = @"%y\0%Y\0%m\0%s\0%T@\0%f\0%l\0";
    private const string WalkFormat = @"%y\0%m\0%P\0%l\0";

    public static Task<char?> Lstat(ISshSession session, string path, CancellationToken ct) =>
        TypeOf(session, $"find {Q(path)} -maxdepth 0 -printf %y", ct);

    public static Task<char?> StatFollow(ISshSession session, string path, CancellationToken ct) =>
        TypeOf(session, $"find -L {Q(path)} -maxdepth 0 -printf %y", ct);

    public static async Task<(char Type, long Size)?> StatFollowWithSize(ISshSession session, string path, CancellationToken ct)
    {
        var result = await session.Exec($"find -L {Q(path)} -maxdepth 0 -printf {Q(@"%y\0%s")} 2>/dev/null", ct);
        var parts = result.Stdout.Split('\0');
        return result.ExitCode == 0 && parts.Length == 2 && parts[0].Length == 1
            ? (parts[0][0], long.Parse(parts[1], CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// Folder contents without following links inside it. If the path itself is a link to a folder, the contents of that folder are shown.
    /// </summary>
    public static async Task<IReadOnlyList<FileEntryView>> List(ISshSession session, string directory, CancellationToken ct)
    {
        var result = await session.Exec($"find -H {Q(directory)} -mindepth 1 -maxdepth 1 -printf {Q(ListFormat)}", ct);
        if (result.ExitCode != 0)
            throw new PanelException(400, $"Could not open folder {directory}: {result.Stderr.Trim()}");
        return FileEntryParser.ParseListing(result.Stdout);
    }

    public static async Task<IReadOnlyList<WalkEntry>> Walk(ISshSession session, string root, CancellationToken ct)
    {
        var result = await session.Exec($"find {Q(root)} -printf {Q(WalkFormat)}", ct);
        if (result.ExitCode != 0)
            throw new PanelException(500, $"Could not read {root}: {result.Stderr.Trim()}");
        return FileEntryParser.ParseWalk(result.Stdout);
    }

    public static async Task<byte[]> ReadAll(ISshSession session, string path, CancellationToken ct)
    {
        await using var stream = await session.OpenRead(path, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    public static async Task Run(ISshSession session, string command, CancellationToken ct)
    {
        var result = await session.Exec(command, ct);
        if (result.ExitCode != 0)
            throw new PanelException(500, result.Stderr.Trim());
    }

    public static async Task RunScript(ISshSession session, IReadOnlyList<string> lines, CancellationToken ct)
    {
        foreach (var chunk in lines.Chunk(ScriptChunk))
            await Run(session, "bash -c " + Q("set -e\n" + string.Join('\n', chunk)), ct);
    }

    private static async Task<char?> TypeOf(ISshSession session, string command, CancellationToken ct)
    {
        var result = await session.Exec(command + " 2>/dev/null", ct);
        return result.ExitCode == 0 && result.Stdout.Length > 0 ? result.Stdout[0] : null;
    }
}