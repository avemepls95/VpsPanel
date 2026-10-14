namespace VpsPanel.Settings;

internal sealed class SettingsPaths(string dataDir)
{
    public static string Default { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VpsPanel");

    public string DataDir { get; } = dataDir;

    public string SettingsFile => Path.Combine(DataDir, "settings.json");

    public string BackupsDir => Path.Combine(DataDir, "backups");
}