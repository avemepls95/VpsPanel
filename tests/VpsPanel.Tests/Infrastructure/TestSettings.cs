using VpsPanel.Settings;

namespace VpsPanel.Tests.Infrastructure;

internal static class TestSettings
{
    public static SettingsStore Store(TempDirectory dir, params ServerSettings[] servers)
    {
        var store = new SettingsStore(new SettingsPaths(dir.Path));
        store.Update(s => s.Servers.AddRange(servers));
        return store;
    }
}