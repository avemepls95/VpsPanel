namespace VpsPanel.Settings;

internal interface ISettingsStore
{
    PanelSettings Read();

    /// <summary>
    /// Rereads the file, applies the change and writes the result, all under one lock.
    /// </summary>
    T Update<T>(Func<PanelSettings, T> change);

    void Update(Action<PanelSettings> change);

    string? GetParseError();
}