namespace VpsPanel;

/// <summary>
/// An error with a user-facing message and an HTTP response code.
/// </summary>
internal class PanelException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}