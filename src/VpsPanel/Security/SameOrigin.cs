namespace VpsPanel.Security;

internal static class SameOrigin
{
    /// <summary>
    /// Checks that the request came from a page of the panel itself. Browsers let any site open a WebSocket
    /// to localhost, and only the Origin header tells such a page apart.
    /// </summary>
    public static bool Check(HttpRequest request) =>
        string.Equals(request.Headers.Origin.ToString(), $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
}