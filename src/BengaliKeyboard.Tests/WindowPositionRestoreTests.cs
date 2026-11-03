using System.IO;
using System.Windows;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Models;
using BengaliKeyboard.Storage;
using BengaliKeyboard.Storage.Configuration;
using BengaliKeyboard.Windows;
using CommunityToolkit.Mvvm.Messaging;
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
