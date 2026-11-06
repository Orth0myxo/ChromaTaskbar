# Feature overview

ChromaTaskbar uses Windows foreground-window notifications with a polling fallback. It considers only the current foreground application, and only while Windows reports that window as maximized. A maximized window normally leaves the taskbar visible; exclusive fullscreen is not required.

The visible client area is captured in memory through Windows GDI and reduced to at most 96×96 pixels. The colour analyzer uses pixel area and spatial support, without boosting saturation. Results are smoothed across changes. Unchanged frames skip colour analysis; a capture is discarded if the foreground target changes before application.

The taskbar renderer remains TranslucentTB. ChromaTaskbar writes its appearance settings only when values change and no more than once per second. It keeps an exact byte-for-byte backup of the original settings and restores them on normal exit.

ChromaTaskbar starts in the background with a notification-area icon. Double-click opens the existing diagnostic/settings window; closing that window hides it while detection continues. The tray menu pauses and resumes detection or exits the app. A named Windows mutex prevents another ChromaTaskbar instance from starting concurrently.

The Windows x64 release carries its .NET runtime. The runtime’s license and third-party notices ship alongside it.

## Limits

- TranslucentTB must be installed and running.
- Protected, minimized, cloaked, unsupported or otherwise inaccessible windows may not be capturable; the last reliable taskbar colour is retained.
- Shell surfaces and the desktop intentionally do not provide sample pixels.
- A forced process termination or system crash can prevent the normal-exit restore. The saved original settings remain in the backup folder for recovery.
- Perceptual results depend on the visible content and UI of each application. ChromaTaskbar does not promise that every extracted colour will match a person's subjective impression.
