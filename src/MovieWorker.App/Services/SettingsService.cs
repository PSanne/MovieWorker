using MovieWorker.Core.Settings;

namespace MovieWorker.App.Services;

/// <summary>Hält die Einstellungen im Speicher und schreibt sie bei Änderungen zurück.</summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store;

    public SettingsService(SettingsStore? store = null)
    {
        _store = store ?? SettingsStore.ForCurrentUser();
        Settings = _store.Load();
    }

    public AppSettings Settings { get; }

    public void Save()
    {
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Einstellungen sind nicht kritisch – ein Fehler beim Speichern darf die Arbeit nicht stören.
        }
    }
}
