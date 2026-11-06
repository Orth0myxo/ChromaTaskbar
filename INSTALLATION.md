# Installation and testing

## Installer

1. Download `ChromaTaskbar-Setup.exe` and double-click it.
2. Choose **Install**. The installer uses the current user's profile and does not need administrator access.
   **Start ChromaTaskbar when I sign in** is enabled by default. Clear it to opt out.
3. Start **ChromaTaskbar** from the Start menu. It runs in the notification area without opening a window.

## Portable version

1. Download `ChromaTaskbar-Portable.zip`.
2. In File Explorer, right-click the ZIP and choose **Extract All**.
3. Open the extracted folder and double-click `Run ChromaTaskbar.cmd`. ChromaTaskbar starts in the notification area.

Double-click the tray icon to open the diagnostic/settings window. Closing the window hides it and leaves colour detection running. Right-click the icon to pause or resume detection, or choose **Exit ChromaTaskbar** for a safe shutdown and TranslucentTB settings restore. Windows may place the icon in the hidden-icons overflow area.

In the ChromaTaskbar window, use **Start ChromaTaskbar when I sign in** to enable or disable the current user's Windows Startup shortcut immediately. This setting does not change TranslucentTB's startup configuration. Uninstall removes only ChromaTaskbar's own login shortcut.

## Test the taskbar behaviour

Make sure TranslucentTB is installed and running, then launch ChromaTaskbar.

1. On the desktop, confirm the taskbar is transparent.
2. Open an application in a normal restored window. The taskbar should remain transparent.
3. Maximize the application. The taskbar should adopt a colour sampled from its visible content.
4. Switch to another maximized application and confirm its colour takes over.
5. Switch to a restored window, minimize the maximized window, or show Start/Task View. The taskbar should return to transparency.
6. Close ChromaTaskbar normally. It restores the original TranslucentTB settings.

The diagnostic window reports the selected application, capture status, extracted colour and timing. It is not itself an eligible colour source.

## Test background controls

1. Confirm startup does not open a ChromaTaskbar window and that its tray icon appears.
2. Double-click the icon to open the diagnostic window, then close it. The app should remain in the tray and continue processing.
3. Use the right-click menu to pause, then resume colour detection.
4. Choose **Exit ChromaTaskbar**. The tray icon should disappear and the original TranslucentTB settings should be restored.

## Uninstall

Use **Uninstall ChromaTaskbar** from the Start menu, or delete the extracted portable folder. Close ChromaTaskbar first so it can restore TranslucentTB settings. The original-settings backups in `%LOCALAPPDATA%\ChromaTaskbar\Backups` are deliberately retained and are not removed by uninstall.
