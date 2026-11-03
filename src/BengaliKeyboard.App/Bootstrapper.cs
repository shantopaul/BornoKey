using System.IO;
using BengaliKeyboard.App.Logging;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Layouts;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.UI.ViewModels;
using BengaliKeyboard.UI.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BengaliKeyboard.App;

/// <summary>
/// Configures Dependency Injection, privacy-redacted Serilog logging, layout initialization, and database startup.
/// </summary>
public sealed class Bootstrapper : IDisposable
{
    private ServiceProvider? _serviceProvider;
    private bool _disposed;

    public IServiceProvider ServiceProvider =>
        _serviceProvider ?? throw new InvalidOperationException("Bootstrapper has not been initialized.");

    public IServiceProvider ConfigureServices(string? customDataDirectory = null, string? customLayoutsDirectory = null)
    {
        var appPaths = new AppPaths(customDataDirectory);
        appPaths.EnsureDirectoriesCreated();

        ConfigureLogging(appPaths);

        var services = new ServiceCollection();

        // Core & Messaging
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // Storage & Configuration
        services.AddSingleton(appPaths);
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        // Configurable Keyboard Layouts
        services.AddSingleton(new LayoutLoader(customLayoutsDirectory));
        services.AddSingleton<ILayoutManager, LayoutManager>();

        // UI ViewModels & Windows
        services.AddSingleton<KeyboardViewModel>();
        services.AddSingleton<KeyboardWindow>();

        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }

    public async Task InitializeCoreServicesAsync(CancellationToken cancellationToken = default)
    {
        var provider = ServiceProvider;

        var settingsService = provider.GetRequiredService<ISettingsService>();
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

        var databaseInitializer = provider.GetRequiredService<DatabaseInitializer>();
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var layoutManager = provider.GetRequiredService<ILayoutManager>();
        await layoutManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
        layoutManager.SetLanguage(settings.ActiveLanguage);
    }

    private static void ConfigureLogging(AppPaths appPaths)
    {
        var logFilePattern = Path.Combine(appPaths.LogsDirectory, "bornokey-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Destructure.With<PrivacyDestructuringPolicy>()
            .WriteTo.File(
                path: logFilePattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SqliteConnection.ClearAllPools();
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
    }
}
