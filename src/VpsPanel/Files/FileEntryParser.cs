using System.Globalization;

namespace VpsPanel.Files;

internal static class FileEntryParser
{
    private const int ListingFields = 7;
    private const int WalkFields = 4;

    public static IReadOnlyList<FileEntryView> ParseListing(string output)
    {
        var fields = output.Split('\0');
        var entries = new List<FileEntryView>();
        for (var i = 0; i + ListingFields <= fields.Length; i += ListingFields)
        {
            var type = TypeOf(fields[i]);
            var isLink = type == FileEntryType.Link;
            entries.Add(new FileEntryView(
                fields[i + 5],
                type,
                isLink ? TargetTypeOf(fields[i + 1]) : null,
                isLink ? fields[i + 6] : null,
                long.Parse(fields[i + 3], CultureInfo.InvariantCulture),
                DateTimeOffset.FromUnixTimeMilliseconds((long)(double.Parse(fields[i + 4], CultureInfo.InvariantCulture) * 1000)),
                fields[i + 2],
                Permissions(fields[i + 2])));
        }

        return entries
            .OrderBy(e => e.Type == FileEntryType.Dir || e.TargetType == FileEntryType.Dir ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<WalkEntry> ParseWalk(string output)
    {
        var fields = output.Split('\0');
        var entries = new List<WalkEntry>();
        for (var i = 0; i + WalkFields <= fields.Length; i += WalkFields)
            entries.Add(new WalkEntry(fields[i][0], fields[i + 1], fields[i + 2], fields[i + 3]));
        return entries;
    }

    public static string Permissions(string mode)
    {
        const string rwx = "rwxrwxrwx";
        var bits = Convert.ToInt32(mode, 8);
        var chars = new char[9];
        for (var i = 0; i < 9; i++)
            chars[i] = (bits & (1 << (8 - i))) != 0 ? rwx[i] : '-';
        return new string(chars);
    }

    private static FileEntryType TypeOf(string value) => value switch
    {
        "f" => FileEntryType.File,
        "d" => FileEntryType.Dir,
        "l" => FileEntryType.Link,
        _ => FileEntryType.Other
    };

    // %Y for a link: the target type, N for a missing target, L for a loop.
    private static FileEntryType TargetTypeOf(string value) => value switch
    {
        "f" => FileEntryType.File,
        "d" => FileEntryType.Dir,
        "N" => FileEntryType.Missing,
        "L" => FileEntryType.Link,
        _ => FileEntryType.Other
    };
}