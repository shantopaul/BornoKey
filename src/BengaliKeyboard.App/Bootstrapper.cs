using System.IO;
using BengaliKeyboard.App.Logging;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Emoji;
using BengaliKeyboard.Input;
using BengaliKeyboard.Input.TSF;
using BengaliKeyboard.Layouts;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Storage.Database;
using BengaliKeyboard.UI.ViewModels;
using BengaliKeyboard.UI.Windows;
using BengaliKeyboard.Windows;
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

        // Bengali Unicode Input Engine & System-Wide Text Injection
        services.AddSingleton<UnicodeProcessor>();
        services.AddSingleton<Win32SendInputInjector>();
        services.AddSingleton<TsfBridgeService>(sp => new TsfBridgeService(sp.GetRequiredService<Win32SendInputInjector>()));
        services.AddSingleton<ITextInjector>(sp => sp.GetRequiredService<TsfBridgeService>());
        services.AddSingleton<IBengaliInputEngine, BengaliInputEngine>();
        services.AddSingleton<OnScreenInputService>();
        services.AddSingleton<PhysicalKeyboardService>();

        // Windows OS Integration, Multi-Monitor Positioning & 4-Mode State Machine
        services.AddSingleton<MonitorService>();
        services.AddSingleton<WindowPositionService>();
        services.AddSingleton<IWindowManagerService>(sp => sp.GetRequiredService<WindowPositionService>());
        services.AddSingleton<StartupService>();
        services.AddSingleton<IStartupService>(sp => sp.GetRequiredService<StartupService>());
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<SystemTrayService>();
        services.AddSingleton<ISystemTrayService>(sp => sp.GetRequiredService<SystemTrayService>());

        // Offline Emoji Catalog, Search & SQLite Usage History
        services.AddSingleton<EmojiRepository>();
        services.AddSingleton<EmojiSearchService>();
        services.AddSingleton<EmojiUsageService>();
        services.AddSingleton<IEmojiService>(sp => sp.GetRequiredService<EmojiUsageService>());

        // UI ViewModels & Windows
        services.AddSingleton<KeyboardViewModel>();
        services.AddSingleton<EmojiViewModel>();
        services.AddSingleton<KeyboardWindow>();
        services.AddSingleton<FloatingIconWindow>();
        services.AddSingleton<EmojiWindow>();

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

        var inputEngine = provider.GetRequiredService<IBengaliInputEngine>();
        inputEngine.SetLanguage(settings.ActiveLanguage);

        var windowPositionService = provider.GetRequiredService<WindowPositionService>();
        settings.WindowState = windowPositionService.ValidateAndClampBounds(settings.WindowState);

        var startupService = provider.GetRequiredService<IStartupService>();
        settings.StartWithWindows = startupService.IsStartupEnabled();
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
