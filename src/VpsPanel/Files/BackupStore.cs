using System.Globalization;
using VpsPanel.Settings;

namespace VpsPanel.Files;

internal sealed class BackupStore(SettingsPaths paths, TimeProvider time)
{
    private const string InvalidChars = "<>:\"/\\|?*";
    private static readonly HashSet<string> ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    private readonly Lock _gate = new();

    public async Task<string> Save(string serverName, string remotePath, Stream content, CancellationToken ct)
    {
        var path = Reserve(serverName, remotePath);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        await content.CopyToAsync(file, ct);
        return path;
    }

    public Task<string> Save(string serverName, string remotePath, byte[] content, CancellationToken ct) =>
        Save(serverName, remotePath, new MemoryStream(content), ct);

    private string Reserve(string serverName, string remotePath)
    {
        var stamp = time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string[] parts =
        [
            paths.BackupsDir,
            Sanitize(serverName),
            stamp,
            .. remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Sanitize)
        ];
        var basePath = Path.Combine(parts);

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            var candidate = basePath;
            for (var i = 2; File.Exists(candidate); i++)
                candidate = $"{basePath}.{i}";
            File.Create(candidate).Dispose();
            return candidate;
        }
    }

    private static string Sanitize(string segment)
    {
        var chars = segment.Select(c => c < 32 || InvalidChars.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).TrimEnd('.', ' ');
        if (result.Length == 0)
            return "_";
        return ReservedNames.Contains(Path.GetFileNameWithoutExtension(result).ToUpperInvariant()) ? "_" + result : result;
    }
}