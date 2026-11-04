namespace GD.SaveEditor.Services.Settings;

public interface ISettingsService
{
    /// <summary>Last saved (or loaded) settings.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after <see cref="Save"/> succeeds.</summary>
    event EventHandler<AppSettings>? SettingsChanged;

    void Save(AppSettings settings);
}
