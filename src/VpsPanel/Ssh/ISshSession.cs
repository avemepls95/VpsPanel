namespace VpsPanel.Ssh;

internal interface ISshSession : IAsyncDisposable
{
    Task<ExecResult> Exec(string command, CancellationToken ct);

    /// <summary>
    /// Runs a command and passes stdout lines as they appear. Returns the exit code.
    /// </summary>
    Task<int?> ExecStreaming(string command, Func<string, Task> onLine, CancellationToken ct);

    /// <summary>
    /// Runs a command, copying stdout into the stream. The result has an empty Stdout, plus stderr and the exit code.
    /// </summary>
    Task<ExecResult> ExecToStream(string command, Stream stdout, CancellationToken ct);

    Task<Stream> OpenRead(string path, CancellationToken ct);

    /// <summary>
    /// Opens a file for writing. Without createNew an existing file is truncated, and its permissions and owner stay the same.
    /// </summary>
    Task<Stream> OpenWrite(string path, bool createNew, CancellationToken ct);
}