using System.Windows;
using System.Windows.Threading;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.UI.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BengaliKeyboard.App;

/// <summary>
/// WPF Application host managing startup state restoration, wakeup handling, and global error recovery.
/// </summary>
public partial class App : Application
{
    private readonly Bootstrapper _bootstrapper;
    private readonly SingleInstanceManager _singleInstanceManager;

    public App(Bootstrapper bootstrapper, SingleInstanceManager singleInstanceManager)
    {
        ArgumentNullException.ThrowIfNull(bootstrapper);
        ArgumentNullException.ThrowIfNull(singleInstanceManager);

        _bootstrapper = bootstrapper;
        _singleInstanceManager = singleInstanceManager;

        RegisterGlobalExceptionHandlers();
        InitializeComponent();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _bootstrapper.ConfigureServices();
            await _bootstrapper.InitializeCoreServicesAsync().ConfigureAwait(true);

            var settingsService = _bootstrapper.ServiceProvider.GetRequiredService<ISettingsService>();
            var keyboardWindow = _bootstrapper.ServiceProvider.GetRequiredService<KeyboardWindow>();

            _singleInstanceManager.StartWakeupListener(() =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (!keyboardWindow.IsVisible)
                    {
                        keyboardWindow.Show();
                    }

                    if (keyboardWindow.WindowState == WindowState.Minimized)
                    {
                        keyboardWindow.WindowState = WindowState.Normal;
                    }
                });
            });

            var savedMode = settingsService.Current.WindowState.Mode;
            if (!settingsService.Current.LaunchMinimized && savedMode != KeyboardMode.HiddenToTray)
            {
                keyboardWindow.ApplySavedWindowGeometry(settingsService.Current.WindowState);
                keyboardWindow.Show();
            }

            Log.Information("BornoKey started successfully in mode {WindowMode}.", savedMode);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during BornoKey application startup.");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("BornoKey shutting down cleanly.");
        _singleInstanceManager.Dispose();
        _bootstrapper.Dispose();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled WPF Dispatcher exception caught.");
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating}).", e.IsTerminating);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved background task exception caught.");
        e.SetObserved();
    }
}
