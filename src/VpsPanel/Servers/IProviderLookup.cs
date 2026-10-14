namespace VpsPanel.Servers;

internal interface IProviderLookup
{
    /// <summary>
    /// Returns the name of the server IP owner, or null if it could not be determined.
    /// </summary>
    Task<string?> Find(string host, CancellationToken ct);
}