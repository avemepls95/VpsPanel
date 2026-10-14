namespace VpsPanel.Settings;

internal sealed class SettingsParseException(string path, string detail)
    : PanelException(500, $"Could not parse the settings file {path}: {detail}");