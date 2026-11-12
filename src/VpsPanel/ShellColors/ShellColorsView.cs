namespace VpsPanel.ShellColors;

internal enum ShellColorsState
{
    On,
    Off,

    /// <summary>
    /// The login shell of the server user is not bash.
    /// </summary>
    Unavailable
}

internal sealed record ShellColorsView(ShellColorsState State);