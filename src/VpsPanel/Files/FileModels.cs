namespace VpsPanel.Files;

internal enum FileEntryType
{
    File,
    Dir,
    Link,
    Other,
    Missing
}

internal sealed record FileEntryView(
    string Name,
    FileEntryType Type,
    FileEntryType? TargetType,
    string? LinkTarget,
    long Size,
    DateTimeOffset Modified,
    string Mode,
    string Permissions);

internal sealed record DirectoryListing(string Path, IReadOnlyList<FileEntryView> Entries);

internal enum FileNotEditableReason
{
    TooLarge,
    Binary,
    NotUtf8
}

internal enum LineSeparator
{
    Lf,
    Crlf
}

internal sealed record FileContentView(
    string Path,
    long Size,
    bool Editable,
    FileNotEditableReason? Reason,
    string? Text,
    string? Sha256,
    bool HasBom,
    LineSeparator LineSeparator);

internal sealed record TextInspection(bool Editable, FileNotEditableReason? Reason, string? Text, bool HasBom, LineSeparator LineSeparator);

/// <summary>
/// A tree object found during traversal: type from find %y, octal permissions, path from the traversal root, link target.
/// </summary>
internal sealed record WalkEntry(char Type, string Mode, string RelativePath, string LinkTarget);

internal sealed record SaveFileRequest(string Path, string Text, string Sha256, bool HasBom, bool Force);

internal sealed record SaveFileResult(string Sha256, string BackupPath);

internal sealed record UploadResult(string Path, string? BackupPath);

internal sealed record CopyRequest(string SourceServerId, string SourcePath, string TargetServerId, string TargetDir, bool Overwrite);