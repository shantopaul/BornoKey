using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Serilog;

namespace BengaliKeyboard.App;

/// <summary>
/// Enforces single-instance execution per Windows user login session using a Local\ SID-scoped Mutex
/// and a CurrentUserOnly Named Pipe for wakeup signaling (TM-04, CHK-02, CHK-05).
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    public const string ShowWindowSignalCommand = "BORNOKEY_SHOW_WINDOW_V1";
    private const int MaxSignalBytes = 128;

    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly bool _isFirstInstance;
    private readonly bool _ownsMutexHandle;
    private CancellationTokenSource? _pipeListenerCts;
    private Task? _pipeListenerTask;
    private bool _disposed;

    public bool IsFirstInstance => _isFirstInstance;

    public string MutexName => _mutexName;

    public string PipeName => _pipeName;

    public SingleInstanceManager(string? customScopeSuffix = null)
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var rawScope = string.IsNullOrWhiteSpace(customScopeSuffix)
            ? userSid
            : $"{userSid}_{customScopeSuffix}";
        var scope = SanitizeKernelIdentifier(rawScope);

        // Use Local\ kernel namespace scoped to the current user SID to prevent cross-session squatting (CHK-02)
        _mutexName = $"Local\\BornoKey_SingleInstance_{scope}";
        _pipeName = $"BornoKey_IPC_{scope}";

        _mutex = new Mutex(initiallyOwned: true, name: _mutexName, out var createdNew);
        if (createdNew)
        {
            _ownsMutexHandle = true;
            _isFirstInstance = true;
        }
        else
        {
            _ownsMutexHandle = false;
            _isFirstInstance = false;
        }
    }

    private static string SanitizeKernelIdentifier(string input)
    {
        Span<char> buffer = stackalloc char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            buffer[i] = char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_';
        }

        return new string(buffer);
    }

    /// <summary>
    /// Starts an asynchronous CurrentUserOnly Named Pipe server that listens for wakeup signals from secondary launches.
    /// </summary>
    public void StartWakeupListener(Action onWakeupRequested)
    {
        ArgumentNullException.ThrowIfNull(onWakeupRequested);
        if (!_isFirstInstance || _pipeListenerCts is not null)
        {
            return;
        }

        _pipeListenerCts = new CancellationTokenSource();
        var ct = _pipeListenerCts.Token;

        _pipeListenerTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    var buffer = new byte[MaxSignalBytes];
                    var bytesRead = await server.ReadAsync(buffer.AsMemory(0, MaxSignalBytes), ct).ConfigureAwait(false);
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    if (string.Equals(message, ShowWindowSignalCommand, StringComparison.Ordinal))
                    {
                        onWakeupRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Non-fatal error in single-instance wakeup pipe listener.");
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    /// <summary>
    /// Signals the already-running primary instance to restore its window.
    /// </summary>
    public async Task<bool> SignalFirstInstanceAsync(int timeoutMs = 1500, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: _pipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await client.ConnectAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
            var payload = Encoding.UTF8.GetBytes(ShowWindowSignalCommand);
            await client.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await client.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _pipeListenerCts?.Cancel();
            _pipeListenerCts?.Dispose();
        }
        catch
        {
            // Ignore cancellation disposal errors
        }

        if (_ownsMutexHandle)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex may be released on a different thread during shutdown
            }
        }

        _mutex.Dispose();
    }
}
