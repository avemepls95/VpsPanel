namespace VpsPanel.Tests.Infrastructure;

/// <summary>
/// A temporary folder that is deleted with its contents on Dispose.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("vpspanel-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}