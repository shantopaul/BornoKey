using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BengaliKeyboard.Core.Models;
using Microsoft.Win32;
using Serilog;

namespace BengaliKeyboard.Clipboard;

/// <summary>
/// Event-driven Windows clipboard listener using <c>AddClipboardFormatListener</c> (<c>WM_CLIPBOARDUPDATE</c>)
/// with zero background polling (Rules.md Section 1.1 &amp; 2.2).
/// Honors all 4 password-manager privacy exclusion formats (CHK-04), pauses during Windows session lock,
/// retries transient <c>CLIPBRD_E_CANT_OPEN</c> errors (Rules.md Scenario 6), and enforces a 64 KB size cap (CHK-06).
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    public const int WmClipboardUpdate = 0x031D;
    public const string FormatExcludeFromMonitorProcessing = "ExcludeClipboardContentFromMonitorProcessing";
    public const string FormatCanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";
    public const string FormatCanUploadToCloudClipboard = "CanUploadToCloudClipboard";
    public const string FormatClipboardViewerIgnore = "ClipboardViewerIgnore";

    private const int MaxClipboardRetryAttempts = 5;
    private const int RetryDelayMs = 15;
    private const long MinEventIntervalTicks = 80 * TimeSpan.TicksPerMillisecond; // Rate-limit rapid clipboard floods

    private HwndSource? _hwndSource;
    private bool _isListening;
    private bool _isSessionLocked;
    private bool _suppressSelfClipboardWrite;
    private long _lastProcessedTimestampTicks;
    private bool _disposed;

    public bool IsListening => _isListening && !_isSessionLocked;

    public event EventHandler<string>? ClipboardTextCaptured;

    public ClipboardMonitor()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isListening)
        {
            return;
        }

        EnsureMessageWindowCreated();
        if (_hwndSource is not null && _hwndSource.Handle != nint.Zero)
        {
            if (AddClipboardFormatListener(_hwndSource.Handle))
            {
                _isListening = true;
            }
        }
    }

    public void Stop()
    {
        if (!_isListening)
        {
            return;
        }

        try
        {
            if (_hwndSource is not null && _hwndSource.Handle != nint.Zero)
            {
                _ = RemoveClipboardFormatListener(_hwndSource.Handle);
            }
        }
        finally
        {
            _isListening = false;
        }
    }

    /// <summary>
    /// Copies text to the Windows clipboard while suppressing self-recapture of the resulting <c>WM_CLIPBOARDUPDATE</c>.
    /// </summary>
    public bool TrySetClipboardTextSafely(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        _suppressSelfClipboardWrite = true;
        try
        {
            for (var attempt = 0; attempt < MaxClipboardRetryAttempts; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }

            return false;
        }
        finally
        {
            _suppressSelfClipboardWrite = false;
        }
    }

    /// <summary>
    /// Evaluates an <see cref="IDataObject"/> against all 4 password-manager / privacy exclusion formats (CHK-04).
    /// Exposed as public static for deterministic unit testing.
    /// </summary>
    public static bool ShouldIgnoreClipboardDataObject(IDataObject? dataObject)
    {
        if (dataObject is null)
        {
            return true;
        }

        try
        {
            // 1. CF_EXCLUDE_CLIPBOARD_CONTENT_FROM_MONITOR_PROCESSING
            if (dataObject.GetDataPresent(FormatExcludeFromMonitorProcessing))
            {
                return true;
            }

            // 2. ClipboardViewerIgnore (used by KeePass / 1Password / Clipdiary)
            if (dataObject.GetDataPresent(FormatClipboardViewerIgnore))
            {
                return true;
            }

            // 3. CanIncludeInClipboardHistory (DWORD == 0 means explicitly excluded)
            if (IsDwordFormatExplicitlyZero(dataObject, FormatCanIncludeInClipboardHistory))
            {
                return true;
            }

            // 4. CanUploadToCloudClipboard (DWORD == 0 means sensitive credential)
            if (IsDwordFormatExplicitlyZero(dataObject, FormatCanUploadToCloudClipboard))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsDwordFormatExplicitlyZero(IDataObject dataObject, string formatName)
    {
        if (!dataObject.GetDataPresent(formatName))
        {
            return false;
        }

        var rawData = dataObject.GetData(formatName);
        return rawData switch
        {
            int intVal => intVal == 0,
            uint uintVal => uintVal == 0,
            byte[] bytes when bytes.Length >= 4 => BitConverter.ToInt32(bytes, 0) == 0,
            byte[] bytes when bytes.Length >= 1 => bytes[0] == 0,
            MemoryStream ms => ReadFirstInt32OrZero(ms) == 0,
            _ => true
        };
    }

    private static int ReadFirstInt32OrZero(MemoryStream stream)
    {
        if (stream.Length == 0)
        {
            return 0;
        }

        var pos = stream.Position;
        try
        {
            stream.Position = 0;
            if (stream.Length >= 4)
            {
                Span<byte> buf = stackalloc byte[4];
                _ = stream.Read(buf);
                return BitConverter.ToInt32(buf);
            }

            return stream.ReadByte();
        }
        finally
        {
            stream.Position = pos;
        }
    }

    private void EnsureMessageWindowCreated()
    {
        if (_hwndSource is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("BornoKey_ClipboardSink")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ParentWindow = new nint(-3) // HWND_MESSAGE
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            handled = true;
            if (_isListening && !_isSessionLocked && !_suppressSelfClipboardWrite)
            {
                var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
                if (nowTicks - _lastProcessedTimestampTicks >= MinEventIntervalTicks)
                {
                    _lastProcessedTimestampTicks = nowTicks;
                    _ = ReadClipboardWithRetryAsync();
                }
            }
        }

        return nint.Zero;
    }

    private async Task ReadClipboardWithRetryAsync()
    {
        for (var attempt = 0; attempt < MaxClipboardRetryAttempts; attempt++)
        {
            try
            {
                var dataObject = System.Windows.Clipboard.GetDataObject();
                if (ShouldIgnoreClipboardDataObject(dataObject))
                {
                    return;
                }

                if (dataObject is null ||
                    (!dataObject.GetDataPresent(DataFormats.UnicodeText) && !dataObject.GetDataPresent(DataFormats.Text)))
                {
                    return;
                }

                var text = System.Windows.Clipboard.GetText(TextDataFormat.UnicodeText);
                if (string.IsNullOrWhiteSpace(text) || text.Length > ClipboardEntry.MaxAllowedContentCharacters)
                {
                    return;
                }

                ClipboardTextCaptured?.Invoke(this, text);
                return;
            }
            catch (COMException)
            {
                await Task.Delay(RetryDelayMs).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Skipped clipboard read due to non-fatal exception.");
                return;
            }
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect)
        {
            _isSessionLocked = true;
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect)
        {
            _isSessionLocked = false;
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _disposed = true;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            Stop();
        }
        finally
        {
            if (_hwndSource is not null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource.Dispose();
                _hwndSource = null;
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);
}
