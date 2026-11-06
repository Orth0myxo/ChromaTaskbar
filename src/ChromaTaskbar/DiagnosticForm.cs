using ChromaTaskbar.Core;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ChromaTaskbar;

internal sealed class DiagnosticForm : Form
{
    private readonly ForegroundMonitor _monitor = new();
    private readonly WindowCapturer _capturer = new();
    private readonly DominantColorAnalyzer _analyzer = new();
    private readonly ColorSmoother _smoother = new();
    private readonly TranslucentTbSettingsController _taskbar;
    private readonly TaskbarStartupCoordinator _taskbarStartup;
    private readonly StartupDiagnosticLog _log;
    private readonly StartupRegistrationController _startupRegistration;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly Label _app = ValueLabel();
    private readonly Label _title = ValueLabel();
    private readonly Label _class = ValueLabel();
    private readonly Label _capture = ValueLabel();
    private readonly Label _color = ValueLabel();
    private readonly CheckBox _startupEnabled = new() { Text = "Start ChromaTaskbar when I sign in", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Panel _preview = new() { Width = 84, Height = 48, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(32, 32, 32) };
    private readonly Icon _windowIcon;
    private string? _taskbarError;
    private CapturedFrame? _lastFrame;
    private ForegroundWindowInfo? _lastFrameTarget;
    private RgbColor? _lastAnalyzedColor;
    private RgbColor? _lastShownColor;
    private int _refreshQueued;
    private bool _paused;
    private bool _allowClose;
    private string? _lastLoggedTarget;
    private string? _lastLoggedCaptureFailure;
    private string? _lastLoggedMonitorError;
    private string? _lastLoggedTaskbarFailure;
    private DateTime _lastMonitorErrorLogUtc;
    private DateTime _lastTaskbarFailureLogUtc;
    private DateTime _lastCaptureFailureLogUtc;
    private bool _firstColorApplyLogged;
    private bool _updatingStartupSetting;

    internal DiagnosticForm(TranslucentTbSettingsController? taskbar = null, StartupDiagnosticLog? log = null,
        StartupRegistrationController? startupRegistration = null)
    {
        _taskbar = taskbar ?? new TranslucentTbSettingsController();
        _taskbarStartup = new TaskbarStartupCoordinator(_taskbar);
        _log = log ?? new StartupDiagnosticLog();
        _startupRegistration = startupRegistration ?? new StartupRegistrationController();
        Text = "ChromaTaskbar";
        _windowIcon = LoadApplicationIcon();
        Icon = _windowIcon;
        ClientSize = new Size(620, 380);
        MinimumSize = new Size(580, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 7,
            AutoSize = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 12));

        AddRow(layout, 0, "Application", _app);
        AddRow(layout, 1, "Window title", _title);
        AddRow(layout, 2, "Window type", _class);
        AddRow(layout, 3, "Status", _capture);
        AddRow(layout, 4, "Taskbar colour", _color);
        AddRow(layout, 5, "Preview", _preview);
        layout.Controls.Add(_startupEnabled, 1, 6);
        Controls.Add(layout);

        try
        {
            _startupEnabled.Checked = _startupRegistration.IsEnabled;
        }
        catch (Exception exception)
        {
            _log.Write("startup.registration.read.failed", Describe(exception));
        }
        _startupEnabled.CheckedChanged += OnStartupSettingChanged;

        _log.Write("startup.form.initialize");
        TryInitializeTaskbar();

        _monitor.ForegroundChanged += OnForegroundChanged;
        _monitor.Error += OnMonitorError;
        _monitor.Start();
        _log.Write("foreground.monitor.started", _monitor.IsHookInstalled
            ? "WinEvent hook and 500 ms polling fallback active."
            : "WinEvent hook unavailable; 500 ms polling fallback active.");
        _monitor.Refresh();
        _timer.Tick += (_, _) => RefreshDiagnostic();
        _timer.Start();
        _log.Write("foreground.refresh.timer.started", "intervalMs=500");
        FormClosing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
        FormClosed += (_, _) =>
        {
            _log.Write("shutdown.begin");
            _timer.Stop();
            _timer.Dispose();
            _monitor.ForegroundChanged -= OnForegroundChanged;
            _monitor.Error -= OnMonitorError;
            _monitor.Dispose();
            _windowIcon.Dispose();
            _startupEnabled.CheckedChanged -= OnStartupSettingChanged;
            try
            {
                _taskbar.Dispose();
                _log.Write("shutdown.ttb.restore.completed");
            }
            catch (Exception exception)
            {
                _log.Write("shutdown.ttb.restore.failed", Describe(exception));
                string backup = _taskbar.BackupPath ?? "No backup path was recorded.";
                MessageBox.Show(
                    $"ChromaTaskbar could not restore the original TranslucentTB settings. The saved original copy is at:\r\n{backup}\r\n\r\nDetails: {exception.Message}",
                    "ChromaTaskbar settings restore failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
    }

    internal bool IsPaused => _paused;
    internal bool IsProcessing => !_paused && _timer.Enabled;
    internal string? StartupError => _taskbarError;

    private void OnStartupSettingChanged(object? sender, EventArgs args)
    {
        if (_updatingStartupSetting) return;
        bool requested = _startupEnabled.Checked;
        try
        {
            _startupRegistration.SetEnabled(requested);
            _log.Write("startup.registration.updated", requested ? "enabled" : "disabled");
        }
        catch (Exception exception)
        {
            _updatingStartupSetting = true;
            try { _startupEnabled.Checked = !requested; }
            finally { _updatingStartupSetting = false; }
            _log.Write("startup.registration.update.failed", Describe(exception));
            _capture.Text = $"Windows startup setting failed: {exception.Message}";
        }
    }

    internal void PauseDetection()
    {
        if (_paused || IsDisposed)
        {
            return;
        }

        _timer.Stop();
        _monitor.Stop();
        _paused = true;
        _log.Write("detection.paused");
        try
        {
            _taskbar.Pause();
            _capture.Text = "Colour detection paused.";
        }
        catch (Exception exception)
        {
            _taskbarError = $"Paused; TTB restore failed: {exception.Message}";
            _log.Write("ttb.pause.restore.failed", Describe(exception));
            _capture.Text = _taskbarError;
        }
    }

    internal void ResumeDetection()
    {
        if (!_paused || IsDisposed)
        {
            return;
        }

        try
        {
            _taskbar.Resume();
        }
        catch (Exception exception)
        {
            _taskbarError = $"Resume failed: {exception.Message}";
            _log.Write("ttb.resume.failed", Describe(exception));
            _capture.Text = _taskbarError;
            return;
        }

        _paused = false;
        _monitor.Start();
        _monitor.Refresh();
        _timer.Start();
        _log.Write("detection.resumed");
        RefreshDiagnostic();
    }

    internal void ExitAndClose()
    {
        if (IsDisposed)
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    private void RefreshDiagnostic()
    {
        bool taskbarReady = TryInitializeTaskbar();
        ForegroundWindowInfo foreground = _monitor.Current;
        ForegroundWindowInfo? target = ForegroundTargetResolver.Select(
            foreground,
            (uint)Environment.ProcessId);
        LogTargetChange(target);
        if (target is null)
        {
            _app.Text = "(no active application)";
            _title.Text = string.Empty;
            _class.Text = string.Empty;
            if (!taskbarReady)
            {
                _capture.Text = _taskbarError ?? "Waiting for TranslucentTB settings.";
                return;
            }

            try
            {
                bool changed = _taskbar.SetDesktopAppearance();
                LogTaskbarSuccess("desktop", changed, null);
                _capture.Text = "Desktop transparency active.";
            }
            catch (Exception exception)
            {
                RecordTaskbarFailure("desktop", exception);
                _capture.Text = _taskbarError!;
            }

            return;
        }

        ForegroundWindowInfo info = target;
        _app.Text = string.IsNullOrWhiteSpace(info.ProcessName) ? "(unavailable)" : info.ProcessName;
        _title.Text = string.IsNullOrWhiteSpace(info.WindowTitle) ? "(no title)" : info.WindowTitle;
        _class.Text = string.IsNullOrWhiteSpace(info.WindowClass) ? "(unavailable)" : info.WindowClass;

        CaptureResult result = _capturer.Capture(info);
        if (!result.Success || result.Frame is null)
        {
            LogCaptureFailure(info, result.FailureReason.ToString(), result.Message ?? string.Empty);
            _capture.Text = $"Failed: {result.FailureReason} — {result.Message}";
            AppendTaskbarError();
            return;
        }

        if (!IsStillCurrentTarget(info))
        {
            _capture.Text = $"Discarded stale capture: {result.Frame.Width}×{result.Frame.Height}, {result.Duration.TotalMilliseconds:F0} ms";
            AppendTaskbarError();
            return;
        }

        if (_lastFrame is not null &&
            _lastFrameTarget is not null &&
            ForegroundTargetResolver.IsSameTarget(_lastFrameTarget, info) &&
            _lastFrame.HasSamePixels(result.Frame))
        {
            if (_lastAnalyzedColor is RgbColor priorSample)
            {
                _smoother.TryAdd(priorSample, DateTime.UtcNow, out RgbColor cachedSmoothed);
                RgbColor cachedShown = _smoother.Current ?? cachedSmoothed;
                _lastShownColor = cachedShown;
                _color.Text = $"{cachedShown}  (unchanged content)";
                _preview.BackColor = Color.FromArgb(cachedShown.R, cachedShown.G, cachedShown.B);
            }

            Stopwatch applyTimer = Stopwatch.StartNew();
            ApplyLastColor();
            applyTimer.Stop();
            _capture.Text = $"Success: {result.Frame.Width}×{result.Frame.Height}, {result.Duration.TotalMilliseconds:F0} ms; unchanged frame, analysis skipped; apply {applyTimer.Elapsed.TotalMilliseconds:F1} ms";
            AppendTaskbarError();
            return;
        }

        Stopwatch analysisTimer = Stopwatch.StartNew();
        DominantColorResult color = _analyzer.Analyze(result.Frame);
        analysisTimer.Stop();

        if (!IsStillCurrentTarget(info))
        {
            _capture.Text = $"Discarded stale capture after analysis: {result.Frame.Width}×{result.Frame.Height}";
            AppendTaskbarError();
            return;
        }

        _smoother.TryAdd(color.Rgb, DateTime.UtcNow, out RgbColor smoothed);
        RgbColor shown = _smoother.Current ?? smoothed;
        _lastFrame = result.Frame;
        _lastFrameTarget = info;
        _lastAnalyzedColor = color.Rgb;
        _lastShownColor = shown;
        _color.Text = $"{shown}  (confidence {color.Confidence:P0})";
        _preview.BackColor = Color.FromArgb(shown.R, shown.G, shown.B);
        Stopwatch apply = Stopwatch.StartNew();

        ApplyColor(shown);
        apply.Stop();

        _capture.Text = $"Success: {result.Frame.Width}×{result.Frame.Height}; capture {result.Duration.TotalMilliseconds:F1} ms, analysis {analysisTimer.Elapsed.TotalMilliseconds:F1} ms, apply {apply.Elapsed.TotalMilliseconds:F1} ms";

        AppendTaskbarError();
    }

    private void OnForegroundChanged(object? sender, ForegroundWindowInfo info)
    {
        if (IsDisposed || !IsHandleCreated || Interlocked.Exchange(ref _refreshQueued, 1) != 0)
        {
            return;
        }

        try
        {
            BeginInvoke((Action)(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (!IsDisposed)
                {
                    RefreshDiagnostic();
                }
            }));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
        }
    }

    private bool IsStillCurrentTarget(ForegroundWindowInfo expected)
    {
        _monitor.Refresh();
        ForegroundWindowInfo? current = ForegroundTargetResolver.Select(
            _monitor.Current,
            (uint)Environment.ProcessId);
        return ForegroundTargetResolver.IsSameTarget(expected, current);
    }

    private void ApplyLastColor()
    {
        if (_lastShownColor is RgbColor color)
        {
            ApplyColor(color);
        }
    }

    private bool TryInitializeTaskbar()
    {
        if (_taskbar.IsReady)
        {
            return true;
        }

        if (_taskbarStartup.TryStart(DateTime.UtcNow, out Exception? failure))
        {
            if (_taskbarError is not null)
            {
                _log.Write("ttb.initialize.recovered", $"attempts={_taskbarStartup.Attempts}");
                if (_taskbarError.StartsWith("TTB settings unavailable:", StringComparison.Ordinal))
                {
                    _taskbarError = null;
                }
            }
            else
            {
                _log.Write("ttb.initialize.ready", $"attempts={_taskbarStartup.Attempts}");
            }

            return true;
        }

        if (failure is not null)
        {
            _taskbarError = $"TTB settings unavailable: {failure.Message}";
            _log.Write("ttb.initialize.failed", $"attempt={_taskbarStartup.Attempts}; retryAtUtc={_taskbarStartup.NextAttemptUtc:O}; {Describe(failure)}");
        }

        return false;
    }

    private void ApplyColor(RgbColor color)
    {
        if (!_taskbar.IsReady)
        {
            return;
        }

        try
        {
            bool changed = _taskbar.ApplyColor(color);
            LogTaskbarSuccess("color", changed, color.ToHex());
        }
        catch (Exception exception)
        {
            RecordTaskbarFailure("color", exception);
        }
    }

    private void LogTaskbarSuccess(string operation, bool changed, string? color)
    {
        if (_taskbarError is not null)
        {
            _log.Write("ttb.operation.recovered", operation);
            if (!_taskbarError.StartsWith("TTB settings unavailable:", StringComparison.Ordinal))
            {
                _taskbarError = null;
            }
        }

        if (operation == "color" && !_firstColorApplyLogged)
        {
            _firstColorApplyLogged = true;
            _log.Write("ttb.first_color_apply", $"color={color}; settingsChanged={changed}");
        }
        else if (changed)
        {
            _log.Write("ttb.settings.updated", $"operation={operation}; color={color ?? "none"}");
        }
    }

    private void RecordTaskbarFailure(string operation, Exception exception)
    {
        _taskbarError = $"TTB {operation} failed: {exception.Message}";
        string key = $"{operation}:{exception.GetType().FullName}:{exception.Message}";
        DateTime now = DateTime.UtcNow;
        if (key != _lastLoggedTaskbarFailure || now - _lastTaskbarFailureLogUtc >= TimeSpan.FromSeconds(30))
        {
            _lastLoggedTaskbarFailure = key;
            _lastTaskbarFailureLogUtc = now;
            _log.Write("ttb.operation.failed", $"operation={operation}; {Describe(exception)}");
        }
    }

    private void LogTargetChange(ForegroundWindowInfo? target)
    {
        string key = target is null
            ? "none"
            : $"{target.ProcessId}:{target.Handle.ToInt64()}:{target.IsMaximized}";
        if (key == _lastLoggedTarget)
        {
            return;
        }

        _lastLoggedTarget = key;
        _log.Write("foreground.target", target is null
            ? "No eligible maximized foreground application."
            : $"process={target.ProcessName}; pid={target.ProcessId}; hwnd=0x{target.Handle.ToInt64():X}; maximized={target.IsMaximized}");
    }

    private void LogCaptureFailure(ForegroundWindowInfo target, string reason, string message)
    {
        string key = $"{target.ProcessId}:{target.Handle.ToInt64()}:{reason}:{message}";
        if (key == _lastLoggedCaptureFailure)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (key != _lastLoggedCaptureFailure || now - _lastCaptureFailureLogUtc >= TimeSpan.FromSeconds(30))
        {
            _lastLoggedCaptureFailure = key;
            _lastCaptureFailureLogUtc = now;
            _log.Write("capture.failed", $"process={target.ProcessName}; pid={target.ProcessId}; reason={reason}; {message}");
        }
    }

    private void OnMonitorError(Exception exception)
    {
        string key = $"{exception.GetType().FullName}:{exception.Message}";
        DateTime now = DateTime.UtcNow;
        if (key == _lastLoggedMonitorError && now - _lastMonitorErrorLogUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastLoggedMonitorError = key;
        _lastMonitorErrorLogUtc = now;
        _log.Write("foreground.monitor.error", Describe(exception));
    }

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";

    private void AppendTaskbarError()
    {
        if (_taskbarError is not null)
        {
            _capture.Text += $" — {_taskbarError}";
        }
    }

    private static Label ValueLabel() => new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Icon LoadApplicationIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ChromaTaskbar.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }

    private static void AddRow(TableLayoutPanel table, int row, string caption, Control value)
    {
        table.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray
        }, 0, row);
        table.Controls.Add(value, 1, row);
    }
}
