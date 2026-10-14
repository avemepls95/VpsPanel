namespace VpsPanel.Ssh;

internal static class ShellQuote
{
    /// <summary>
    /// Wraps the value in single quotes, each ' inside becomes '\''.
    /// </summary>
    public static string Q(string value) => "'" + value.Replace("'", "'\\''") + "'";
}