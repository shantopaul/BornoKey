using System.Windows;
using System.Windows.Threading;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Interfaces;
using BengaliKeyboard.Input;
using BengaliKeyboard.UI.ViewModels;
using BengaliKeyboard.UI.Windows;
using BengaliKeyboard.Windows;
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
            var physicalKeyboardService = _bootstrapper.ServiceProvider.GetRequiredService<PhysicalKeyboardService>();
            var windowPositionService = _bootstrapper.ServiceProvider.GetRequiredService<WindowPositionService>();
            var globalHotkeyService = _bootstrapper.ServiceProvider.GetRequiredService<GlobalHotkeyService>();
            var systemTrayService = _bootstrapper.ServiceProvider.GetRequiredService<SystemTrayService>();
            var keyboardViewModel = _bootstrapper.ServiceProvider.GetRequiredService<KeyboardViewModel>();
            var keyboardWindow = _bootstrapper.ServiceProvider.GetRequiredService<KeyboardWindow>();
            var floatingIconWindow = _bootstrapper.ServiceProvider.GetRequiredService<FloatingIconWindow>();

            floatingIconWindow.ActiveLanguage = settingsService.Current.ActiveLanguage;

            // Wire Global Hotkeys
            globalHotkeyService.LanguageToggleRequested += async (_, _) => await keyboardViewModel.ToggleLanguageAsync().ConfigureAwait(true);
            globalHotkeyService.VisibilityToggleRequested += (_, _) => windowPositionService.ToggleVisibility();
            globalHotkeyService.Initialize();

            // Wire System Tray Icon & 10-Item Context Menu
            systemTrayService.ExitRequested += async (_, _) =>
            {
                var currentGeometry = keyboardWindow.CaptureCurrentWindowGeometry();
                await windowPositionService.SaveWindowStateAsync(currentGeometry).ConfigureAwait(true);
                Shutdown(0);
            };
            systemTrayService.Initialize();

            // Wire FloatingIconWindow events
            floatingIconWindow.RestoreRequested += (_, _) => windowPositionService.RestoreLastVisibleMode();
            floatingIconWindow.LanguageToggleRequested += async (_, _) => await keyboardViewModel.ToggleLanguageAsync().ConfigureAwait(true);
            floatingIconWindow.BadgePositionChanged += (_, coords) =>
            {
                var state = settingsService.Current.WindowState;
                state.IconLeft = coords.Left;
                state.IconTop = coords.Top;
                windowPositionService.ScheduleDebouncedSave(state);
            };

            // Wire KeyboardWindow resize/DPI debounced persistence
            keyboardWindow.WindowGeometryChanged += (_, updatedState) =>
            {
                windowPositionService.ScheduleDebouncedSave(updatedState);
            };

            // Wire 4-Mode State Machine Presenter
            windowPositionService.RegisterModePresenter((mode, clampedState) =>
            {
                keyboardWindow.ApplySavedWindowGeometry(clampedState);
                floatingIconWindow.Left = clampedState.IconLeft;
                floatingIconWindow.Top = clampedState.IconTop;
                floatingIconWindow.Topmost = clampedState.AlwaysOnTop;

                switch (mode)
                {
                    case KeyboardMode.Expanded:
                    case KeyboardMode.Compact:
                        if (floatingIconWindow.IsVisible)
                        {
                            floatingIconWindow.Hide();
                        }
                        if (!keyboardWindow.IsVisible)
                        {
                            keyboardWindow.Show();
                        }
                        break;

                    case KeyboardMode.MinimalIcon:
                        if (keyboardWindow.IsVisible)
                        {
                            keyboardWindow.Hide();
                        }
                        if (!floatingIconWindow.IsVisible)
                        {
                            floatingIconWindow.Show();
                        }
                        break;

                    case KeyboardMode.HiddenToTray:
                        if (keyboardWindow.IsVisible)
                        {
                            keyboardWindow.Hide();
                        }
                        if (floatingIconWindow.IsVisible)
                        {
                            floatingIconWindow.Hide();
                        }
                        break;
                }
            });

            if (settingsService.Current.PhysicalKeyboardHookEnabled)
            {
                physicalKeyboardService.Start();
            }

            _singleInstanceManager.StartWakeupListener(() =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    windowPositionService.RestoreLastVisibleMode();
                });
            });

            var initialMode = settingsService.Current.LaunchMinimized
                ? KeyboardMode.HiddenToTray
                : settingsService.Current.WindowState.Mode;

            windowPositionService.ApplyWindowMode(initialMode);

            Log.Information("BornoKey started successfully in mode {WindowMode}.", initialMode);
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
