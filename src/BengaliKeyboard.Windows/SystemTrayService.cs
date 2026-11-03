using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using BengaliKeyboard.Core.Enums;
using BengaliKeyboard.Core.Events;
using BengaliKeyboard.Core.Interfaces;
using CommunityToolkit.Mvvm.Messaging;

namespace BengaliKeyboard.Windows;

/// <summary>
/// Manages BornoKey's persistent Windows Notification Area (System Tray) icon via Win32 <c>Shell_NotifyIconW</c>
/// and renders the 10-item context menu (PRD.md Section 3.8 &amp; Phases.md Section 5.1).
/// </summary>
public sealed class SystemTrayService :
    ISystemTrayService,
    IRecipient<LanguageChangedMessage>,
    IRecipient<WindowModeChangedMessage>
{
    public const string MenuKeyShowKeyboard = "ShowKeyboard";
    public const string MenuKeyCompactMode = "CompactMode";
    public const string MenuKeyMinimalIcon = "MinimalIcon";
    public const string MenuKeyHideKeyboard = "HideKeyboard";
    public const string MenuKeyToggleLanguage = "ToggleLanguage";
    public const string MenuKeyEmoji = "Emoji";
    public const string MenuKeyClipboard = "ClipboardHistory";
    public const string MenuKeySettings = "Settings";
    public const string MenuKeyStartWithWindows = "StartWithWindows";
    public const string MenuKeyExit = "Exit";

    private const uint TrayIconId = 1001;

    private readonly IWindowManagerService _windowManagerService;
    private readonly IBengaliInputEngine _inputEngine;
    private readonly ILayoutManager _layoutManager;
    private readonly IStartupService _startupService;
    private readonly ISettingsService _settingsService;
    private readonly IMessenger _messenger;
    private readonly bool _enableShellIconRegistration;

    private readonly Dictionary<string, MenuItem> _menuItemsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _checkedStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _headerTexts = new(StringComparer.Ordinal);
    private readonly object _stateLock = new();
    private ContextMenu? _contextMenu;
    private HwndSource? _trayHwndSource;
    private nint _loadedIconHandle = nint.Zero;
    private bool _ownsLoadedIconHandle;
    private bool _isShellIconAdded;
    private bool _isInitialized;
    private bool _disposed;

    public bool IsInitialized => _isInitialized;

    public string CurrentTooltipText { get; private set; } = "BornoKey — বাংলা";

    /// <summary>
    /// Ordered list of the 10 context menu action keys defined in PRD.md Section 3.8.
    /// </summary>
    public static IReadOnlyList<string> OrderedMenuKeys { get; } =
    [
        MenuKeyShowKeyboard,
        MenuKeyCompactMode,
        MenuKeyMinimalIcon,
        MenuKeyHideKeyboard,
        MenuKeyToggleLanguage,
        MenuKeyEmoji,
        MenuKeyClipboard,
        MenuKeySettings,
        MenuKeyStartWithWindows,
        MenuKeyExit
    ];

    public event EventHandler? EmojiPanelRequested;

    public event EventHandler? ClipboardPanelRequested;

    public event EventHandler? SettingsWindowRequested;

    public event EventHandler? ExitRequested;

    public SystemTrayService(
        IWindowManagerService windowManagerService,
        IBengaliInputEngine inputEngine,
        ILayoutManager layoutManager,
        IStartupService startupService,
        ISettingsService settingsService,
        IMessenger? messenger = null,
        bool enableShellIconRegistration = true)
    {
        ArgumentNullException.ThrowIfNull(windowManagerService);
        ArgumentNullException.ThrowIfNull(inputEngine);
        ArgumentNullException.ThrowIfNull(layoutManager);
        ArgumentNullException.ThrowIfNull(startupService);
        ArgumentNullException.ThrowIfNull(settingsService);

        _windowManagerService = windowManagerService;
        _inputEngine = inputEngine;
        _layoutManager = layoutManager;
        _startupService = startupService;
        _settingsService = settingsService;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _enableShellIconRegistration = enableShellIconRegistration;

        _messenger.Register<LanguageChangedMessage>(this);
        _messenger.Register<WindowModeChangedMessage>(this);
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isInitialized)
        {
            return;
        }

        BuildContextMenu();

        if (_enableShellIconRegistration)
        {
            EnsureTrayMessageWindowCreated();
            RegisterShellTrayIcon();
        }

        _isInitialized = true;
        UpdateTrayState();
    }

    public void UpdateTrayState()
    {
        if (_disposed || !_isInitialized)
        {
            return;
        }

        var activeLanguage = _layoutManager.ActiveLanguage;
        var currentMode = _windowManagerService.CurrentMode;
        var startupEnabled = _startupService.IsStartupEnabled();

        var langLabel = activeLanguage == LanguageMode.Bangla ? "বাংলা" : "ENG";
        CurrentTooltipText = $"BornoKey — {langLabel} ({currentMode})";

        lock (_stateLock)
        {
            _checkedStates[MenuKeyShowKeyboard] = currentMode == KeyboardMode.Expanded;
            _checkedStates[MenuKeyCompactMode] = currentMode == KeyboardMode.Compact;
            _checkedStates[MenuKeyMinimalIcon] = currentMode == KeyboardMode.MinimalIcon;
            _checkedStates[MenuKeyHideKeyboard] = currentMode == KeyboardMode.HiddenToTray;
            _checkedStates[MenuKeyStartWithWindows] = startupEnabled;

            _headerTexts[MenuKeyToggleLanguage] = activeLanguage == LanguageMode.Bangla
                ? "Bangla / English (বাংলা ✓)"
                : "Bangla / English (ENG ✓)";
        }

        if (_contextMenu is not null)
        {
            if (_contextMenu.Dispatcher.CheckAccess())
            {
                ApplyVisualStateToMenuItems();
            }
            else
            {
                _ = _contextMenu.Dispatcher.InvokeAsync(ApplyVisualStateToMenuItems);
            }
        }

        if (_isShellIconAdded && _trayHwndSource is not null)
        {
            var data = CreateNotifyIconData(_trayHwndSource.Handle, NativeMethods.NifTip);
            _ = NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
        }
    }

    private void ApplyVisualStateToMenuItems()
    {
        lock (_stateLock)
        {
            foreach (var (key, isChecked) in _checkedStates)
            {
                if (_menuItemsByKey.TryGetValue(key, out var item))
                {
                    item.IsChecked = isChecked;
                }
            }

            foreach (var (key, header) in _headerTexts)
            {
                if (_menuItemsByKey.TryGetValue(key, out var item))
                {
                    item.Header = header;
                }
            }
        }
    }

    public void ShowNotification(string title, string message)
    {
        if (_disposed || !_isShellIconAdded || _trayHwndSource is null)
        {
            return;
        }

        var safeTitle = string.IsNullOrWhiteSpace(title) ? "BornoKey" : title.Trim();
        if (safeTitle.Length > 60)
        {
            safeTitle = safeTitle[..60];
        }

        var safeMessage = (message ?? string.Empty).Trim();
        if (safeMessage.Length > 250)
        {
            safeMessage = safeMessage[..250];
        }

        var data = CreateNotifyIconData(_trayHwndSource.Handle, NativeMethods.NifInfo);
        data.szInfoTitle = safeTitle;
        data.szInfo = safeMessage;
        data.dwInfoFlags = NativeMethods.NiifInfo;
        _ = NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
    }

    /// <summary>
    /// Simulates a left-click or double-click on the tray icon, restoring the last visible keyboard mode
    /// (PRD.md Section 3.8).
    /// </summary>
    public void HandleTrayPrimaryClick()
    {
        _windowManagerService.RestoreLastVisibleMode();
        UpdateTrayState();
    }

    /// <summary>
    /// Executes one of the 10 context menu commands by its action key.
    /// </summary>
    public async Task<bool> ExecuteMenuActionAsync(string menuKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(menuKey))
        {
            return false;
        }

        switch (menuKey)
        {
            case MenuKeyShowKeyboard:
                _windowManagerService.ApplyWindowMode(KeyboardMode.Expanded);
                break;

            case MenuKeyCompactMode:
                _windowManagerService.ApplyWindowMode(KeyboardMode.Compact);
                break;

            case MenuKeyMinimalIcon:
                _windowManagerService.ApplyWindowMode(KeyboardMode.MinimalIcon);
                break;

            case MenuKeyHideKeyboard:
                _windowManagerService.ApplyWindowMode(KeyboardMode.HiddenToTray);
                break;

            case MenuKeyToggleLanguage:
                _layoutManager.ToggleLanguage();
                _inputEngine.SetLanguage(_layoutManager.ActiveLanguage);
                _settingsService.Current.ActiveLanguage = _layoutManager.ActiveLanguage;
                await _settingsService.SaveAsync(_settingsService.Current, cancellationToken).ConfigureAwait(true);
                break;

            case MenuKeyEmoji:
                EmojiPanelRequested?.Invoke(this, EventArgs.Empty);
                break;

            case MenuKeyClipboard:
                ClipboardPanelRequested?.Invoke(this, EventArgs.Empty);
                break;

            case MenuKeySettings:
                SettingsWindowRequested?.Invoke(this, EventArgs.Empty);
                break;

            case MenuKeyStartWithWindows:
                var nextStartupState = !_startupService.IsStartupEnabled();
                if (_startupService.SetStartupEnabled(nextStartupState))
                {
                    _settingsService.Current.StartWithWindows = nextStartupState;
                    await _settingsService.SaveAsync(_settingsService.Current, cancellationToken).ConfigureAwait(true);
                }
                break;

            case MenuKeyExit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;

            default:
                return false;
        }

        UpdateTrayState();
        return true;
    }

    public bool IsMenuItemChecked(string menuKey)
    {
        lock (_stateLock)
        {
            return _checkedStates.TryGetValue(menuKey, out var isChecked) && isChecked;
        }
    }

    public string? GetMenuItemHeader(string menuKey)
    {
        lock (_stateLock)
        {
            return _headerTexts.TryGetValue(menuKey, out var header) ? header : null;
        }
    }

    public void Receive(LanguageChangedMessage message) => UpdateTrayState();

    public void Receive(WindowModeChangedMessage message) => UpdateTrayState();

    private void BuildContextMenu()
    {
        _menuItemsByKey.Clear();
        _contextMenu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint
        };

        AddMenuItem(MenuKeyShowKeyboard, "Show Keyboard", isCheckable: true);
        AddMenuItem(MenuKeyCompactMode, "Compact Mode", isCheckable: true);
        AddMenuItem(MenuKeyMinimalIcon, "Minimal Icon", isCheckable: true);
        AddMenuItem(MenuKeyHideKeyboard, "Hide Keyboard", isCheckable: true);
        _contextMenu.Items.Add(new Separator());

        AddMenuItem(MenuKeyToggleLanguage, "Bangla / English (বাংলা ✓)", isCheckable: false);
        AddMenuItem(MenuKeyEmoji, "Emoji", isCheckable: false);
        AddMenuItem(MenuKeyClipboard, "Clipboard History", isCheckable: false);
        AddMenuItem(MenuKeySettings, "Settings", isCheckable: false);
        _contextMenu.Items.Add(new Separator());

        AddMenuItem(MenuKeyStartWithWindows, "Start with Windows", isCheckable: true);
        _contextMenu.Items.Add(new Separator());
        AddMenuItem(MenuKeyExit, "Exit", isCheckable: false);
    }

    private void AddMenuItem(string key, string header, bool isCheckable)
    {
        var item = new MenuItem
        {
            Header = header,
            Tag = key,
            IsCheckable = isCheckable
        };

        item.Click += async (_, _) => await ExecuteMenuActionAsync(key).ConfigureAwait(true);

        lock (_stateLock)
        {
            _checkedStates[key] = false;
            _headerTexts[key] = header;
        }

        _menuItemsByKey[key] = item;
        _contextMenu?.Items.Add(item);
    }

    private void EnsureTrayMessageWindowCreated()
    {
        if (_trayHwndSource is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("BornoKey_SystemTraySink")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0
        };

        _trayHwndSource = new HwndSource(parameters);
        _trayHwndSource.AddHook(WndProc);
    }

    private void RegisterShellTrayIcon()
    {
        if (_trayHwndSource is null || _trayHwndSource.Handle == nint.Zero)
        {
            return;
        }

        _loadedIconHandle = ResolveTrayIconHandle(out _ownsLoadedIconHandle);
        var data = CreateNotifyIconData(
            _trayHwndSource.Handle,
            NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip);

        _isShellIconAdded = NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data);
    }

    private NativeMethods.NotifyIconData CreateNotifyIconData(nint hwnd, uint flags)
    {
        return new NativeMethods.NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            hWnd = hwnd,
            uID = TrayIconId,
            uFlags = flags,
            uCallbackMessage = NativeMethods.WmTrayIconCallback,
            hIcon = _loadedIconHandle,
            szTip = CurrentTooltipText,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private static nint ResolveTrayIconHandle(out bool ownsHandle)
    {
        ownsHandle = false;

        string[] candidatePaths =
        [
            Path.Combine(AppContext.BaseDirectory, "assets", "Icons", "tray-icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "tray-icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "Icons", "tray-icon.ico")
        ];

        foreach (var path in candidatePaths)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full))
                {
                    var handle = NativeMethods.LoadImage(
                        nint.Zero,
                        full,
                        NativeMethods.ImageIcon,
                        16,
                        16,
                        NativeMethods.LrLoadFromFile);

                    if (handle != nint.Zero)
                    {
                        ownsHandle = true;
                        return handle;
                    }
                }
            }
            catch
            {
            }
        }

        return NativeMethods.LoadIcon(nint.Zero, new nint(NativeMethods.IdiApplication));
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmTrayIconCallback)
        {
            var mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg is NativeMethods.WmLButtonUp or NativeMethods.WmLButtonDblClk)
            {
                HandleTrayPrimaryClick();
                handled = true;
            }
            else if (mouseMsg is NativeMethods.WmRButtonUp or NativeMethods.WmContextMenu)
            {
                UpdateTrayState();
                if (_contextMenu is not null)
                {
                    _contextMenu.IsOpen = true;
                }
                handled = true;
            }
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messenger.UnregisterAll(this);

        if (_isShellIconAdded && _trayHwndSource is not null && _trayHwndSource.Handle != nint.Zero)
        {
            var data = CreateNotifyIconData(_trayHwndSource.Handle, 0);
            _ = NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
            _isShellIconAdded = false;
        }

        if (_ownsLoadedIconHandle && _loadedIconHandle != nint.Zero)
        {
            _ = NativeMethods.DestroyIcon(_loadedIconHandle);
            _loadedIconHandle = nint.Zero;
            _ownsLoadedIconHandle = false;
        }

        if (_trayHwndSource is not null)
        {
            _trayHwndSource.RemoveHook(WndProc);
            _trayHwndSource.Dispose();
            _trayHwndSource = null;
        }
    }
}
