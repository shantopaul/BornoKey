using BengaliKeyboard.Core.Models;

namespace BengaliKeyboard.Core.Interfaces;

/// <summary>
/// Provides atomic loading, clamping, and saving of application configuration (settings.json).
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}
