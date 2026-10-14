namespace VpsPanel.Settings;

internal static class SettingsExtensions
{
    public static ServerSettings GetServer(this PanelSettings settings, string id) =>
        settings.Servers.FirstOrDefault(s => s.Id == id) ?? throw new PanelException(404, "Server not found.");
}