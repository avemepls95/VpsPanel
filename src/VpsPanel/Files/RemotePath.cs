namespace VpsPanel.Files;

internal static class RemotePath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
            throw new PanelException(400, $"The path must start with /: {path}");

        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }

    public static string Name(string normalized) =>
        normalized == "/" ? "" : normalized[(normalized.LastIndexOf('/') + 1)..];

    public static string Parent(string normalized)
    {
        if (normalized == "/")
            return "/";
        var index = normalized.LastIndexOf('/');
        return index == 0 ? "/" : normalized[..index];
    }

    public static string Combine(string directory, string name) =>
        directory == "/" ? "/" + name : directory + "/" + name;

    public static bool IsInside(string candidate, string root) =>
        candidate == root || candidate.StartsWith(root == "/" ? "/" : root + "/", StringComparison.Ordinal);

    public static void ValidateFileName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new PanelException(400, $"Invalid file name: {name}");
    }
}