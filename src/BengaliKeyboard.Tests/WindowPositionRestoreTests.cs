using System.IO;
using System.Windows;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Input;
using BengaliKeyboard.Input.TSF;
using BengaliKeyboard.Layouts;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.UI.Windows;
using BengaliKeyboard.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using Xunit;

namespace BengaliKeyboard.Tests;

public sealed class WindowPositionRestoreTests : IDisposable
{
    private readonly string _tempDir;

    public WindowPositionRestoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BornoKey_Phase4Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void CalculateIntersectionRatio_ShouldEnforceSixtyPercentVisibilityThreshold()
    {
        var workArea = new Rect(0, 0, 1920, 1040);

        // 100% inside monitor
        var fullInside = new Rect(400, 600, 850, 310);
        Assert.Equal(1.0, MonitorService.CalculateIntersectionRatio(fullInside, workArea), precision: 3);

        // 70% inside monitor (595px of 850px width inside right edge)
        var seventyPercentInside = new Rect(1920 - 595, 600, 850, 310);
        var ratio70 = MonitorService.CalculateIntersectionRatio(seventyPercentInside, workArea);
        Assert.True(ratio70 >= MonitorService.MinimumRequiredIntersectionRatio);

        // Only 40% inside monitor (340px of 850px width inside right edge)
        var fortyPercentInside = new Rect(1920 - 340, 600, 850, 310);
        var ratio40 = MonitorService.CalculateIntersectionRatio(fortyPercentInside, workArea);
        Assert.True(ratio40 < MonitorService.MinimumRequiredIntersectionRatio);
    }

    [Fact]
    public void ValidateAndClampBounds_ShouldPreserveSecondaryMonitor_WhenConnected_AndSnapToPrimary_WhenDisconnected()
    {
        var primaryDisplay = new MonitorBoundsInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            IsPrimary = true,
            MonitorBounds = new Rect(0, 0, 1920, 1080),
            WorkingArea = new Rect(0, 0, 1920, 1040),
            DpiScale = 1.0
        };

        var secondaryDisplay = new MonitorBoundsInfo
        {
            DeviceName = @"\\.\DISPLAY2",
            IsPrimary = false,
            MonitorBounds = new Rect(1920, 0, 1920, 1080),
            WorkingArea = new Rect(1920, 0, 1920, 1040),
            DpiScale = 1.25
        };

        var isSecondaryConnected = true;
        var monitorService = new MonitorService(() =>
            isSecondaryConnected ? [primaryDisplay, secondaryDisplay] : [primaryDisplay]);

        var appPaths = new AppPaths(_tempDir);
        var settingsService = new JsonSettingsService(appPaths);
        using var positionService = new WindowPositionService(settingsService, monitorService);

        // 1. Saved on secondary monitor while secondary monitor is connected
        var savedOnSecondary = new WindowStateConfig
        {
            Mode = KeyboardMode.Expanded,
            Width = 850,
            Height = 310,
            Left = 2300,
            Top = 600,
            IconLeft = 3600,
            IconTop = 700,
            MonitorDeviceId = @"\\.\DISPLAY2"
        };

        var validatedConnected = positionService.ValidateAndClampBounds(savedOnSecondary);
        Assert.Equal(@"\\.\DISPLAY2", validatedConnected.MonitorDeviceId);
        Assert.Equal(2300, validatedConnected.Left);
        Assert.Equal(600, validatedConnected.Top);
        Assert.Equal(1.25, validatedConnected.DpiScale);

        // 2. Disconnect secondary monitor -> must snap centered horizontally & 40px above taskbar on primary monitor
        isSecondaryConnected = false;
        var validatedDisconnected = positionService.ValidateAndClampBounds(savedOnSecondary);

        Assert.Equal(@"\\.\DISPLAY1", validatedDisconnected.MonitorDeviceId);
        Assert.Equal(1.0, validatedDisconnected.DpiScale);
        Assert.Equal(535, validatedDisconnected.Left); // (1920 - 850) / 2 = 535
        Assert.Equal(690, validatedDisconnected.Top);  // 1040 - 310 - 40 = 690

        // Floating MinimalIcon badge must also be snapped onto primary monitor
        Assert.InRange(validatedDisconnected.IconLeft, 0, 1920 - WindowPositionService.MinimalIconWidth);
        Assert.InRange(validatedDisconnected.IconTop, 0, 1040 - WindowPositionService.MinimalIconHeight);
    }

    [Fact]
    public async Task FourModeStateMachine_ShouldTransitionReversibly_RememberLastVisibleMode_AndPersistState()
    {
        var primaryDisplay = new MonitorBoundsInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            IsPrimary = true,
            MonitorBounds = new Rect(0, 0, 1920, 1080),
            WorkingArea = new Rect(0, 0, 1920, 1040),
            DpiScale = 1.0
        };

        var monitorService = new MonitorService(() => [primaryDisplay]);
        var appPaths = new AppPaths(_tempDir);
        var settingsService = new JsonSettingsService(appPaths);
        var messenger = new WeakReferenceMessenger();
        using var positionService = new WindowPositionService(settingsService, monitorService, messenger);

        KeyboardMode? presentedMode = null;
        positionService.RegisterModePresenter((mode, _) => presentedMode = mode);

        WindowModeChangedMessage? lastMessage = null;
        messenger.Register<WindowModeChangedMessage>(this, (_, msg) => lastMessage = msg);

        // 1. Switch Expanded -> Compact
        positionService.ApplyWindowMode(KeyboardMode.Compact);
        Assert.Equal(KeyboardMode.Compact, positionService.CurrentMode);
        Assert.Equal(KeyboardMode.Compact, presentedMode);
        Assert.Equal(KeyboardMode.Compact, settingsService.Current.WindowState.LastVisibleMode);
        Assert.NotNull(lastMessage);
        Assert.Equal(KeyboardMode.Compact, lastMessage!.NewMode);

        // 2. Switch Compact -> MinimalIcon (must preserve LastVisibleMode == Compact)
        positionService.ApplyWindowMode(KeyboardMode.MinimalIcon);
        Assert.Equal(KeyboardMode.MinimalIcon, positionService.CurrentMode);
        Assert.Equal(KeyboardMode.MinimalIcon, presentedMode);
        Assert.Equal(KeyboardMode.Compact, settingsService.Current.WindowState.LastVisibleMode);

        // 3. Clicking floating [বাংলা] icon calls RestoreLastVisibleMode -> returns to Compact
        positionService.RestoreLastVisibleMode();
        Assert.Equal(KeyboardMode.Compact, positionService.CurrentMode);
        Assert.Equal(KeyboardMode.Compact, presentedMode);

        // 4. Switch to Expanded -> HideToTray -> ToggleVisibility restores Expanded
        positionService.ApplyWindowMode(KeyboardMode.Expanded);
        positionService.ToggleVisibility();
        Assert.Equal(KeyboardMode.HiddenToTray, positionService.CurrentMode);
        Assert.Equal(KeyboardMode.Expanded, settingsService.Current.WindowState.LastVisibleMode);

        positionService.ToggleVisibility();
        Assert.Equal(KeyboardMode.Expanded, positionService.CurrentMode);

        // 5. Persist and reload from disk
        await positionService.SaveWindowStateAsync(settingsService.Current.WindowState);
        var reloadedSettings = await settingsService.LoadAsync();
        Assert.Equal(KeyboardMode.Expanded, reloadedSettings.WindowState.Mode);
    }

    [Fact]
    public void ValidateAndClampBounds_ShouldSupportNegativeLeftMonitorCoordinates_AndClampToSmallerSecondaryMonitor()
    {
        var primaryDisplay = new MonitorBoundsInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            IsPrimary = true,
            MonitorBounds = new Rect(0, 0, 2560, 1440),
            WorkingArea = new Rect(0, 0, 2560, 1400),
            DpiScale = 1.0
        };

        // Secondary display positioned to the LEFT of the primary display (negative X coordinates) with smaller width (1280x720)
        var leftSecondaryDisplay = new MonitorBoundsInfo
        {
            DeviceName = @"\\.\DISPLAY2",
            IsPrimary = false,
            MonitorBounds = new Rect(-1280, 0, 1280, 720),
            WorkingArea = new Rect(-1280, 0, 1280, 680),
            DpiScale = 1.0
        };

        var monitorService = new MonitorService(() => [primaryDisplay, leftSecondaryDisplay]);
        var appPaths = new AppPaths(_tempDir);
        var settingsService = new JsonSettingsService(appPaths);
        using var positionService = new WindowPositionService(settingsService, monitorService);

        var savedOnLeftMonitor = new WindowStateConfig
        {
            Mode = KeyboardMode.Expanded,
            Width = 1100,
            Height = 310,
            Left = -1200,
            Top = 200,
            MonitorDeviceId = @"\\.\DISPLAY2"
        };

        var clamped = positionService.ValidateAndClampBounds(savedOnLeftMonitor);
        Assert.Equal(@"\\.\DISPLAY2", clamped.MonitorDeviceId);
        Assert.InRange(clamped.Left, -1280, -180);
        Assert.Equal(200, clamped.Top);
    }

    [Fact]
    public void StartupService_ShouldEnforceQuotedExecutablePath_RejectInjection_AndPersistToRegistry()
    {
        var pathWithSpaces = @"C:\Program Files\BornoKey\BengaliKeyboard.App.exe";
        var formatted = StartupService.FormatQuotedStartupCommand(pathWithSpaces);
        Assert.Equal("\"C:\\Program Files\\BornoKey\\BengaliKeyboard.App.exe\" --startup", formatted);

        Assert.Throws<ArgumentException>(() =>
            StartupService.FormatQuotedStartupCommand(@"C:\Program Files\BornoKey\BengaliKeyboard.App.exe & calc.exe"));

        Assert.Throws<ArgumentException>(() =>
            StartupService.FormatQuotedStartupCommand(@"C:\Program Files\BornoKey\script.bat"));

        var testRegSubKey = $@"Software\BornoKey_Tests\Run_{Guid.NewGuid():N}";
        try
        {
            var startupService = new StartupService(testRegSubKey, pathWithSpaces);
            Assert.False(startupService.IsStartupEnabled());

            Assert.True(startupService.SetStartupEnabled(true));
            Assert.True(startupService.IsStartupEnabled());

            Assert.True(startupService.SetStartupEnabled(false));
            Assert.False(startupService.IsStartupEnabled());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\BornoKey_Tests", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void GlobalHotkeyService_ShouldParseModifierCombinations_AndDispatchHotkeyEvents()
    {
        Assert.True(GlobalHotkeyService.TryParseHotkeyString("Ctrl+Space", out var mod1, out var vk1));
        Assert.Equal(NativeMethods.ModControl | NativeMethods.ModNoRepeat, mod1);
        Assert.Equal(0x20u, vk1);

        Assert.True(GlobalHotkeyService.TryParseHotkeyString("Alt+Shift+B", out var mod2, out var vk2));
        Assert.Equal(NativeMethods.ModAlt | NativeMethods.ModShift | NativeMethods.ModNoRepeat, mod2);
        Assert.Equal((uint)'B', vk2);

        Assert.True(GlobalHotkeyService.TryParseHotkeyString("Ctrl+F12", out _, out var vkF12));
        Assert.Equal(0x7Bu, vkF12);

        Assert.False(GlobalHotkeyService.TryParseHotkeyString("Space", out _, out _));

        var appPaths = new AppPaths(_tempDir);
        var settingsService = new JsonSettingsService(appPaths);
        using var hotkeyService = new GlobalHotkeyService(settingsService);

        var languageToggled = false;
        var visibilityToggled = false;
        hotkeyService.LanguageToggleRequested += (_, _) => languageToggled = true;
        hotkeyService.VisibilityToggleRequested += (_, _) => visibilityToggled = true;

        Assert.True(hotkeyService.DispatchHotkeyById(GlobalHotkeyService.HotkeyIdToggleLanguage));
        Assert.True(languageToggled);

        Assert.True(hotkeyService.DispatchHotkeyById(GlobalHotkeyService.HotkeyIdToggleVisibility));
        Assert.True(visibilityToggled);
    }

    [Fact]
    public void NonActivatingWindowResizer_ShouldHitTestAllEightEdgesAndCorners_AndClampSteppedAndDraggedDimensions()
    {
        // 1. Hit-testing all 8 edges/corners + interior
        Assert.Equal(ResizeEdge.TopLeft, NonActivatingWindowResizer.HitTestResizeEdge(new Point(2, 2), 850, 310));
        Assert.Equal(ResizeEdge.TopRight, NonActivatingWindowResizer.HitTestResizeEdge(new Point(848, 2), 850, 310));
        Assert.Equal(ResizeEdge.BottomLeft, NonActivatingWindowResizer.HitTestResizeEdge(new Point(2, 308), 850, 310));
        Assert.Equal(ResizeEdge.BottomRight, NonActivatingWindowResizer.HitTestResizeEdge(new Point(848, 308), 850, 310));
        Assert.Equal(ResizeEdge.Left, NonActivatingWindowResizer.HitTestResizeEdge(new Point(2, 150), 850, 310));
        Assert.Equal(ResizeEdge.Right, NonActivatingWindowResizer.HitTestResizeEdge(new Point(848, 150), 850, 310));
        Assert.Equal(ResizeEdge.Top, NonActivatingWindowResizer.HitTestResizeEdge(new Point(400, 2), 850, 310));
        Assert.Equal(ResizeEdge.Bottom, NonActivatingWindowResizer.HitTestResizeEdge(new Point(400, 308), 850, 310));
        Assert.Equal(ResizeEdge.None, NonActivatingWindowResizer.HitTestResizeEdge(new Point(400, 150), 850, 310));

        // 2. Proportional [+] / [-] step scaling and scale percentage calculation
        var (increasedW, increasedH) = NonActivatingWindowResizer.CalculateSteppedSize(
            850, 310, NonActivatingWindowResizer.StepScaleFactor, 440, 1600, 180, 600);
        Assert.Equal(918, increasedW);
        Assert.Equal(335, increasedH);
        Assert.Equal(108, NonActivatingWindowResizer.CalculateScalePercentage(increasedW, 850));

        var (decreasedW, decreasedH) = NonActivatingWindowResizer.CalculateSteppedSize(
            850, 310, -NonActivatingWindowResizer.StepScaleFactor, 440, 1600, 180, 600);
        Assert.Equal(782, decreasedW);
        Assert.Equal(285, decreasedH);
        Assert.Equal(92, NonActivatingWindowResizer.CalculateScalePercentage(decreasedW, 850));

        // 3. Drag resize bounds calculation & min/max clamping
        var startBounds = new Rect(400, 600, 850, 310);
        var draggedBottomRight = NonActivatingWindowResizer.CalculateResizedBounds(
            ResizeEdge.BottomRight, startBounds, new Vector(150, 70), 440, 1600, 180, 600);
        Assert.Equal(new Rect(400, 600, 1000, 380), draggedBottomRight);

        var draggedTopLeftClampedToMin = NonActivatingWindowResizer.CalculateResizedBounds(
            ResizeEdge.TopLeft, startBounds, new Vector(900, 900), 440, 1600, 180, 600);
        Assert.Equal(440, draggedTopLeftClampedToMin.Width);
        Assert.Equal(180, draggedTopLeftClampedToMin.Height);
        Assert.Equal(startBounds.Right, draggedTopLeftClampedToMin.Right);
        Assert.Equal(startBounds.Bottom, draggedTopLeftClampedToMin.Bottom);
    }

    [Fact]
    public void ComputeUniformScale_ShouldMaintainAspectRatioFreeScaling_AcrossAllWindowSizes()
    {
        // Default 850x310 -> 1.0 uniform scale
        Assert.Equal(1.0, KeyboardWindow.ComputeUniformScale(850, 310), precision: 3);

        // Wide-short 1600x310 (188% width, 100% height) -> must stay 1.0 so glyphs never stretch horizontally
        Assert.Equal(1.0, KeyboardWindow.ComputeUniformScale(1600, 310), precision: 3);

        // Narrow-tall 518x310 (61% width, 100% height) -> scales uniformly by 518/850 (~0.609) without squishing
        var narrowScale = KeyboardWindow.ComputeUniformScale(518, 310);
        Assert.InRange(narrowScale, 0.60, 0.62);

        // Compact 440x180 -> bounded safely above 0.55
        var minScale = KeyboardWindow.ComputeUniformScale(440, 180);
        Assert.InRange(minScale, 0.55, 0.60);

        // Ultrawide tall 1600x600 -> bounded safely at <= 1.85
        var maxScale = KeyboardWindow.ComputeUniformScale(1600, 600);
        Assert.InRange(maxScale, 1.80, 1.85);
    }

    [Fact]
    public async Task SystemTrayService_ShouldExposeTenContextMenuItems_AndSyncModeLanguageAndStartupStates()
    {
        Assert.Equal(10, SystemTrayService.OrderedMenuKeys.Count);

        var testRegSubKey = $@"Software\BornoKey_Tests\Tray_{Guid.NewGuid():N}";
        try
        {
            var appPaths = new AppPaths(_tempDir);
            var settingsService = new JsonSettingsService(appPaths);
            var monitorService = new MonitorService(() =>
            [
                new MonitorBoundsInfo
                {
                    DeviceName = @"\\.\DISPLAY1",
                    IsPrimary = true,
                    MonitorBounds = new Rect(0, 0, 1920, 1080),
                    WorkingArea = new Rect(0, 0, 1920, 1040)
                }
            ]);

            var messenger = new WeakReferenceMessenger();
            using var windowPositionService = new WindowPositionService(settingsService, monitorService, messenger);
            var layoutLoader = new LayoutLoader(Path.Combine(_tempDir, "Layouts"));
            var layoutManager = new LayoutManager(layoutLoader, messenger);
            await layoutManager.InitializeAsync();

            using var inputEngine = new BengaliInputEngine(new UnicodeProcessor(), new Win32SendInputInjector(), null, settingsService, messenger);
            var startupService = new StartupService(testRegSubKey, @"C:\Program Files\BornoKey\BengaliKeyboard.App.exe");

            Exception? staError = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    using var trayService = new SystemTrayService(
                        windowPositionService,
                        inputEngine,
                        layoutManager,
                        startupService,
                        settingsService,
                        messenger,
                        enableShellIconRegistration: false);

                    trayService.Initialize();
                    Assert.True(trayService.IsInitialized);
                    Assert.True(trayService.IsMenuItemChecked(SystemTrayService.MenuKeyShowKeyboard));

                    // Switch to Compact mode via menu
                    trayService.ExecuteMenuActionAsync(SystemTrayService.MenuKeyCompactMode).GetAwaiter().GetResult();
                    Assert.Equal(KeyboardMode.Compact, windowPositionService.CurrentMode);
                    Assert.True(trayService.IsMenuItemChecked(SystemTrayService.MenuKeyCompactMode));

                    // Hide to Tray via menu, then left-click tray icon to restore last visible mode (Compact)
                    trayService.ExecuteMenuActionAsync(SystemTrayService.MenuKeyHideKeyboard).GetAwaiter().GetResult();
                    Assert.Equal(KeyboardMode.HiddenToTray, windowPositionService.CurrentMode);
                    Assert.True(trayService.IsMenuItemChecked(SystemTrayService.MenuKeyHideKeyboard));

                    trayService.HandleTrayPrimaryClick();
                    Assert.Equal(KeyboardMode.Compact, windowPositionService.CurrentMode);

                    // Toggle language via tray menu
                    trayService.ExecuteMenuActionAsync(SystemTrayService.MenuKeyToggleLanguage).GetAwaiter().GetResult();
                    Assert.Equal(LanguageMode.English, layoutManager.ActiveLanguage);
                    Assert.Contains("ENG ✓", trayService.GetMenuItemHeader(SystemTrayService.MenuKeyToggleLanguage));

                    // Toggle Start with Windows via tray menu
                    trayService.ExecuteMenuActionAsync(SystemTrayService.MenuKeyStartWithWindows).GetAwaiter().GetResult();
                    Assert.True(startupService.IsStartupEnabled());
                    Assert.True(trayService.IsMenuItemChecked(SystemTrayService.MenuKeyStartWithWindows));
                }
                catch (Exception ex)
                {
                    staError = ex;
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            if (staError is not null)
            {
                throw new AggregateException("STA SystemTrayService test failed.", staError);
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\BornoKey_Tests", throwOnMissingSubKey: false);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }
}
