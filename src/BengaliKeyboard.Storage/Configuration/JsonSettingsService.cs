using System.Text.Json;
using System.Text.Json.Serialization;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Core.Models;
using Serilog;

namespace BengaliKeyboard.Storage.Configuration;

/// <summary>
/// Thread-safe, atomic JSON configuration manager for %LOCALAPPDATA%\BengaliKeyboard\Config\settings.json.
/// Enforces MaxDepth, value clamping, and corrupted file recovery (Rules.md #1/#3, AS-03, CHK-06).
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    public const int MaxAllowedSettingsFileBytes = 256 * 1024; // 256 KB ceiling

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _appPaths;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private AppSettings _current = new();

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? SettingsChanged;

    public JsonSettingsService(AppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _appPaths = appPaths;
        _current.NormalizeAndClamp();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _appPaths.EnsureDirectoriesCreated();
            var settingsPath = _appPaths.SettingsFilePath;

            if (!File.Exists(settingsPath))
            {
                _current = new AppSettings();
                _current.NormalizeAndClamp();
                await WriteSettingsAtomicallyNoLockAsync(_current, cancellationToken).ConfigureAwait(false);
                return _current;
            }

            try
            {
                var fileInfo = new FileInfo(settingsPath);
                if (fileInfo.Length > MaxAllowedSettingsFileBytes)
                {
                    throw new JsonException($"Settings file exceeds maximum size limit ({fileInfo.Length} bytes).");
                }

                await using var stream = new FileStream(
                    settingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                var loaded = await JsonSerializer
                    .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false) ?? new AppSettings();

                loaded.NormalizeAndClamp();
                _current = loaded;
                return _current;
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                var backupPath = Path.Combine(_appPaths.ConfigDirectory, "settings.corrupt.bak");
                Log.Warning(
                    ex,
                    "Corrupted or unreadable settings.json detected. Backing up to {BackupPath} and restoring defaults.",
                    backupPath);

                try
                {
                    if (File.Exists(settingsPath))
                    {
                        File.Copy(settingsPath, backupPath, overwrite: true);
                    }
                }
                catch
                {
                    // Ignore backup copy failure
                }

                _current = new AppSettings();
                _current.NormalizeAndClamp();
                await WriteSettingsAtomicallyNoLockAsync(_current, cancellationToken).ConfigureAwait(false);
                return _current;
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.NormalizeAndClamp();

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current = settings;
            await WriteSettingsAtomicallyNoLockAsync(_current, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }

        SettingsChanged?.Invoke(this, _current);
    }

    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var defaults = new AppSettings();
        defaults.NormalizeAndClamp();
        await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSettingsAtomicallyNoLockAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        _appPaths.EnsureDirectoriesCreated();
        var targetPath = _appPaths.SettingsFilePath;
        var tempPath = $"{targetPath}.tmp";

        await using (var tempStream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer
                .SerializeAsync(tempStream, settings, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            await tempStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, targetPath, overwrite: true);
    }
}
