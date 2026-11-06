# Troubleshooting

## The taskbar does not change colour

- Confirm TranslucentTB is installed and running.
- Confirm the application window is maximized, not merely large or snapped.
- Check that the maximized application is the foreground window.
- Read the ChromaTaskbar status line for capture or settings errors.
- Start and test with a normal desktop application; protected or GPU-backed content can block screen capture.

## The taskbar stays transparent

This is expected on the desktop, with a normal restored or minimized window, or while Start, Search or Task View is in front. Only the current foreground maximized application controls the colour.

## The ChromaTaskbar window is not visible

This is expected after startup. ChromaTaskbar runs in the notification area. Double-click its icon to open the diagnostic window; closing that window hides it without stopping detection. Windows may put the icon in the hidden-icons overflow area. Right-click the icon to pause/resume detection or choose **Exit ChromaTaskbar**.

If the tray icon does not appear, start ChromaTaskbar again and check for a startup warning. Only one instance is allowed; an existing instance keeps running in the tray.

## TranslucentTB settings or backup error

ChromaTaskbar refuses to write settings if it cannot read the settings file or create its original-settings backup. Check that TranslucentTB is installed and that the current user can write to `%LOCALAPPDATA%\ChromaTaskbar\Backups`.

If taskbar initialization was delayed, ChromaTaskbar retries automatically. For startup, foreground-monitor, capture, and TranslucentTB errors, inspect `%LOCALAPPDATA%\ChromaTaskbar\Logs\startup.log`. The log records process names and window handles but does not record window titles or screenshots.

## Settings were not restored after an abnormal shutdown

On a normal close, ChromaTaskbar restores the original settings. If the process was forcibly terminated or Windows stopped unexpectedly, find the original copy under `%LOCALAPPDATA%\ChromaTaskbar\Backups`. Close TranslucentTB before manually restoring that file to its package `RoamingState\settings.json` location. Do not delete the backup until the settings have been restored and verified.

## Uninstall

Close ChromaTaskbar normally before uninstalling. The uninstaller leaves the settings backup folder in place as a recovery safeguard.
