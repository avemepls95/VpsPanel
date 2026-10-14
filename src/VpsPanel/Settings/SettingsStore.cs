using System.Text.Json;

namespace VpsPanel.Settings;

internal sealed class SettingsStore(SettingsPaths paths) : ISettingsStore
{
    private readonly Lock _gate = new();

    public PanelSettings Read()
    {
        lock (_gate)
            return Load();
    }

    public T Update<T>(Func<PanelSettings, T> change)
    {
        lock (_gate)
        {
            var settings = Load();
            var result = change(settings);
            Save(settings);
            return result;
        }
    }

    public void Update(Action<PanelSettings> change) =>
        Update(settings =>
        {
            change(settings);
            return true;
        });

    public string? GetParseError()
    {
        try
        {
            Read();
            return null;
        }
        catch (SettingsParseException e)
        {
            return e.Message;
        }
    }

    private PanelSettings Load()
    {
        if (!File.Exists(paths.SettingsFile))
            return new PanelSettings();

        try
        {
            return JsonSerializer.Deserialize<PanelSettings>(File.ReadAllText(paths.SettingsFile), SettingsJson.Options)
                   ?? new PanelSettings();
        }
        catch (JsonException e)
        {
            throw new SettingsParseException(paths.SettingsFile, e.Message);
        }
    }

    private void Save(PanelSettings settings)
    {
        Directory.CreateDirectory(paths.DataDir);
        var temp = paths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJson.Options));
        File.Move(temp, paths.SettingsFile, overwrite: true);
    }
}