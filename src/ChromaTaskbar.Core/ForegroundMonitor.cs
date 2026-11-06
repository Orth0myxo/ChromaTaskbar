using System.Threading;

namespace ChromaTaskbar.Core;

public sealed class ForegroundMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly NativeMethods.WinEventDelegate _winEventProc;
    private readonly System.Threading.Timer _pollTimer;
    private readonly ForegroundWindowHistory _history = new();

    private SynchronizationContext? _syncContext;
    private IntPtr _hook;
    private string _lastSignature = string.Empty;
    private ForegroundWindowInfo _current = WindowInfoResolver.Empty;
    private bool _started;
    private bool _disposed;

    public ForegroundMonitor()
    {
        _winEventProc = OnWinEvent;
        _pollTimer = new System.Threading.Timer(Poll, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<ForegroundWindowInfo>? ForegroundChanged;

    public event Action<Exception>? Error;

    public ForegroundWindowInfo Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public ForegroundWindowInfo LastExternal
    {
        get
        {
            lock (_gate)
            {
                return _history.LastExternal;
            }
        }
    }

    public bool IsHookInstalled => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _syncContext ??= SynchronizationContext.Current;

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _pollTimer.Change(0, 500);
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Refresh() => Poll(null);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _pollTimer.Dispose();
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_disposed || eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            return;
        }

        try
        {
            ForegroundWindowInfo info = hwnd == IntPtr.Zero
                ? WindowInfoResolver.FromForegroundWindow()
                : WindowInfoResolver.FromWindow(hwnd);

            Publish(info);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void Poll(object? state)
    {
        if (_disposed || !_started)
        {
            return;
        }

        try
        {
            Publish(WindowInfoResolver.FromForegroundWindow());
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void ReportError(Exception exception)
    {
        try
        {
            Error?.Invoke(exception);
        }
        catch
        {
        }
    }

    private void Publish(ForegroundWindowInfo info)
    {
        string signature = string.Join(
            '|',
            info.Handle.ToInt64(),
            info.ProcessId,
            info.ContentProcessId?.ToString() ?? "-",
            info.WindowClass,
            info.WindowTitle,
            info.IsVisible,
            info.IsMinimized,
            info.IsMaximized,
            info.IsCloaked,
            info.Bounds?.ToString() ?? "-");

        lock (_gate)
        {
            if (signature == _lastSignature)
            {
                return;
            }

            _lastSignature = signature;
            _current = info;
            _history.TryUpdate(info);
        }

        if (_syncContext is not null)
        {
            _syncContext.Post(_ => Raise(info), null);
        }
        else
        {
            Raise(info);
        }
    }

    private void Raise(ForegroundWindowInfo info)
    {
        if (_disposed)
        {
            return;
        }

        ForegroundChanged?.Invoke(this, info);
    }
}
