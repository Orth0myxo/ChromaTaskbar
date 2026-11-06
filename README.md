# ChromaTaskbar

ChromaTaskbar changes the Windows 11 taskbar colour to reflect the foreground maximized application. It runs in the notification area, uses TranslucentTB to render the taskbar, and keeps the taskbar transparent on the desktop and with normal, minimized, or Windows shell windows.

## Screenshots and demo

There are no genuine application screenshots in this repository yet. To see the behavior on your PC, start ChromaTaskbar with TranslucentTB running, maximize Edge or File Explorer, then switch between them. The taskbar follows the active maximized window. Double-click the ChromaTaskbar tray icon to view its diagnostic window, including the detected application, capture status, and colour preview.

## Features

- Adapts only to the foreground maximized application.
- Keeps the taskbar transparent on the desktop and Windows shell surfaces.
- Captures visible application content in memory and samples a representative colour.
- Smooths changes when switching between applications.
- Starts quietly in the background with a notification-area icon.
- Preserves the original TranslucentTB settings in a backup and restores them on normal exit.
- Does not save screenshots, send network traffic, inject into applications, or modify Explorer.

## Requirements

- Windows 11, 64-bit.
- TranslucentTB installed and running.
- No Visual Studio, .NET SDK, or separate .NET runtime is required for the release packages. The .NET runtime is bundled.

## Install

### Installer

1. Download `ChromaTaskbar-Setup.exe` from the v1.0.0 GitHub release.
2. Run it and choose **Install**. Installation is per user and does not require administrator access.
3. Start **ChromaTaskbar** from the Start menu. It starts in the notification area without opening a window.

### Portable version

1. Download `ChromaTaskbar-Portable.zip` from the v1.0.0 GitHub release.
2. In File Explorer, right-click the ZIP and choose **Extract All**.
3. Open the extracted folder and double-click `Run ChromaTaskbar.cmd`.

See [INSTALLATION.md](INSTALLATION.md) for testing and uninstall instructions.

## System tray controls

- **Double-click** the icon to open the diagnostic window.
- **Open Settings** shows the same window from the right-click menu.
- **Pause colour detection** stops sampling and restores the original TranslucentTB settings while paused.
- **Resume colour detection** restarts monitoring and colour updates.
- **Exit ChromaTaskbar** stops the background process and restores the original settings.

Closing the diagnostic window hides it to the tray. It does not exit the application. Windows may place the icon in the hidden-icons area.

## How it works

ChromaTaskbar watches foreground-window changes with Windows accessibility events and a polling fallback. It accepts only the current foreground application when Windows reports it as maximized. It captures the visible window content with Windows GDI, reduces the sample, and analyzes pixels in memory. The colour result is smoothed before ChromaTaskbar updates TranslucentTB's appearance settings. Settings writes are limited to changes and throttled. Before changing settings, ChromaTaskbar saves an exact copy of the original file under `%LOCALAPPDATA%\ChromaTaskbar\Backups`.

## Known limitations

- TranslucentTB must be installed and running for taskbar appearance changes.
- Protected, inaccessible, or some GPU-rendered windows may not capture successfully. ChromaTaskbar retains the last reliable colour.
- Only the foreground maximized application controls the colour. A large restored window, background window, desktop, Start menu, or Task View does not.
- Exclusive fullscreen is not required; a maximized window normally leaves the taskbar visible.
- A forced termination or system crash can prevent the normal-exit settings restoration. The original settings backup is retained for recovery.
- The sampled colour is based on visible content and may not match every user's preferred interpretation of an application's colour.

## Licence

MIT. See [LICENSE](LICENSE). Runtime licence and third-party notices are included in each release package.

## More information

- [Feature overview](FEATURES.md)
- [Installation and testing](INSTALLATION.md)
- [Troubleshooting](TROUBLESHOOTING.md)
