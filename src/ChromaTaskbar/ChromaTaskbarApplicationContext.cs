using System.Drawing;
using System.Windows.Forms;

namespace ChromaTaskbar;

internal sealed class ChromaTaskbarApplicationContext : ApplicationContext
{
    private readonly DiagnosticForm _settings;
    private readonly StartupDiagnosticLog _log;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon? _trayImage;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private bool _exiting;
    private bool _resourcesDisposed;

    internal ChromaTaskbarApplicationContext(DiagnosticForm? settings = null, StartupDiagnosticLog? log = null)
    {
        _log = log ?? new StartupDiagnosticLog();
        _settings = settings ?? new DiagnosticForm(log: _log);
        _menu = new ContextMenuStrip();
        _menu.Items.Add("Open Settings", null, (_, _) => OpenSettings());
        _menu.Items.Add(new ToolStripSeparator());
        _pauseResumeItem = new ToolStripMenuItem("Pause colour detection");
        _pauseResumeItem.Click += (_, _) => TogglePause();
        _menu.Items.Add(_pauseResumeItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit ChromaTaskbar", null, (_, _) => ExitApplication());
        _menu.Opening += (_, _) => UpdatePauseMenu();

        NotifyIcon? trayIcon = null;
        try
        {
            _trayImage = CreateTrayIcon();
            trayIcon = new NotifyIcon
            {
                Icon = _trayImage,
                Text = "ChromaTaskbar",
                ContextMenuStrip = _menu
            };
            _trayIcon = trayIcon;
            trayIcon.DoubleClick += (_, _) => OpenSettings();
            trayIcon.Visible = true;
            if (_settings.StartupError is string startupError)
            {
                trayIcon.ShowBalloonTip(
                    5000,
                    "ChromaTaskbar needs attention",
                    $"TranslucentTB is not ready yet. ChromaTaskbar will retry. {startupError} Log: {_log.Path}",
                    ToolTipIcon.Warning);
            }
        }
        catch
        {
            trayIcon?.Dispose();
            _menu.Dispose();
            if (_trayImage is not null)
            {
                _trayImage.Dispose();
            }

            _settings.ExitAndClose();
            throw;
        }
    }

    internal bool IsTrayIconVisible => _trayIcon.Visible;
    internal bool IsSettingsVisible => _settings.Visible;
    internal bool IsPaused => _settings.IsPaused;
    internal bool IsProcessing => _settings.IsProcessing;

    internal void OpenSettings()
    {
        if (_exiting || _settings.IsDisposed)
        {
            return;
        }

        if (!_settings.Visible)
        {
            _settings.Show();
        }

        if (_settings.WindowState == FormWindowState.Minimized)
        {
            _settings.WindowState = FormWindowState.Normal;
        }

        _settings.Activate();
    }

    internal void TogglePause()
    {
        if (_exiting)
        {
            return;
        }

        if (_settings.IsPaused)
        {
            _settings.ResumeDetection();
        }
        else
        {
            _settings.PauseDetection();
        }

        UpdatePauseMenu();
    }

    internal void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _trayIcon.Visible = false;
        _settings.ExitAndClose();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        Cleanup();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cleanup();
        }

        base.Dispose(disposing);
    }

    private void Cleanup()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        _exiting = true;
        if (!_settings.IsDisposed)
        {
            _settings.ExitAndClose();
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _menu.Dispose();
        _trayImage?.Dispose();
    }

    private void UpdatePauseMenu()
    {
        _pauseResumeItem.Text = _settings.IsPaused
            ? "Resume colour detection"
            : "Pause colour detection";
    }

    private static Icon CreateTrayIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ChromaTaskbar.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
    }
}
