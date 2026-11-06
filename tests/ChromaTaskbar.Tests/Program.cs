using ChromaTaskbar.Core;
using ChromaTaskbar;
using System.Drawing;
using System.Text;
using System.Text.Json.Nodes;

var checks = new (string Name, Action Run)[]
{
    ("RGB HEX formatting", () => Equal("#12ABEF", new RgbColor(0x12, 0xAB, 0xEF).ToHex())),
    ("dominant colour histogram", TestDominantColor),
    ("achromatic fallback", TestAchromaticFallback),
    ("dominant colour follows broad pixel regions", TestDominantColorDistributions),
    ("YouTube-like red interface does not overpower content", TestYoutubeLikeInterface),
    ("temporal smoothing", TestSmoothing),
    ("last external foreground window", TestForegroundWindowHistory),
    ("only maximized foreground windows are eligible", TestMaximizedWindowEligibility),
    ("desktop and shell surfaces resolve to transparency", TestDesktopTargetResolution),
    ("application switching selects the newest foreground window", TestForegroundSwitching),
    ("switching application colours updates temporary TTB settings", TestSwitchingColors),
    ("returning to desktop restores transparency", TestDesktopTransparency),
    ("stale captures cannot match a newer foreground target", TestStaleCapturePrevention),
    ("unchanged captured frames are recognized", TestUnchangedFrame),
    ("TranslucentTB backup failure blocks writes", TestTranslucentTbBackupFailure),
    ("TranslucentTB settings initialization failure", TestTranslucentTbSettingsInitializationFailure),
    ("TranslucentTB successful initialization", TestTranslucentTbSuccessfulInitialization),
    ("TranslucentTB settings update and restore", TestTranslucentTbSettings),
    ("TranslucentTB delayed initialization retries after backoff", TestTaskbarStartupRetry),
    ("startup diagnostic log records initialization events", TestStartupDiagnosticLog),
    ("login startup registration is idempotent and removable", TestStartupRegistration),
    ("single-instance lease prevents duplicate startup", TestSingleInstanceLease),
    ("tray lifecycle keeps processing until explicit exit", TestTrayLifecycle),
    ("taskbar rectangle exclusion", TestRectangleExclusion),
    ("captured frame validates dimensions", TestFrameValidation)
};

int failed = 0;
foreach ((string name, Action run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void TestDominantColor()
{
    uint[] pixels = Enumerable.Repeat(0xFFDE2010u, 120)
        .Concat(Enumerable.Repeat(0xFF2040E0u, 40))
        .Concat(Enumerable.Repeat(0xFF101010u, 40))
        .ToArray();
    DominantColorResult result = new DominantColorAnalyzer().Analyze(pixels);
    Equal("#DE2010", result.Hex);
    Equal(200, result.TotalPixels);
    Equal(200, result.ConsideredPixels);
    True(!result.IsFallback, "Chromatic pixels should not use fallback.");
}

static void TestAchromaticFallback()
{
    uint[] pixels = [0xFF202020, 0xFF808080, 0xFFE0E0E0, 0xFF000000];
    DominantColorResult result = new DominantColorAnalyzer().Analyze(pixels);
    True(result.IsFallback, "An achromatic dominant region should be marked as fallback.");
    True(result.TotalPixels == 4, "All opaque pixels should be counted.");
    True(result.Hex.Length == 7, "Fallback should still return a HEX colour.");
}

static void TestDominantColorDistributions()
{
    const int width = 96;
    const int height = 96;
    uint[] pixels = Enumerable.Repeat(0xFF808080u, width * height).ToArray();
    Fill(pixels, width, new Rectangle(0, 0, 70, height), 0xFF2868D0u);
    Fill(pixels, width, new Rectangle(70, 0, 20, height), 0xFFD03020u);
    DominantColorResult result = new DominantColorAnalyzer().Analyze(
        new CapturedFrame(width, height, pixels, Rectangle.Empty, DateTime.UtcNow));
    Equal("#2868D0", result.Hex);
}

static void TestYoutubeLikeInterface()
{
    const int width = 96;
    const int height = 96;
    uint[] pixels = Enumerable.Repeat(0xFF111111u, width * height).ToArray();
    Fill(pixels, width, new Rectangle(8, 12, 80, 68), 0xFF858585u); // Large neutral video region.
    Fill(pixels, width, new Rectangle(0, 0, 76, 8), 0xFFE62117u); // Small logo/header accents.
    Fill(pixels, width, new Rectangle(12, 82, 72, 2), 0xFFE62117u); // Progress bar.
    Fill(pixels, width, new Rectangle(2, 12, 5, 10), 0xFFE62117u); // Small player icon.

    DominantColorResult result = new DominantColorAnalyzer().Analyze(
        new CapturedFrame(width, height, pixels, Rectangle.Empty, DateTime.UtcNow));
    Equal("#858585", result.Hex);
}

static void Fill(uint[] pixels, int width, Rectangle area, uint color)
{
    for (int y = area.Top; y < area.Bottom; y++)
    {
        Array.Fill(pixels, color, y * width + area.Left, area.Width);
    }
}

static void TestSmoothing()
{
    ColorSmoother smoother = new(minInterval: TimeSpan.Zero, timeConstant: TimeSpan.FromMilliseconds(400));
    DateTime start = DateTime.UtcNow;
    True(smoother.TryAdd(new RgbColor(240, 20, 20), start, out _), "First sample should initialize the smoother.");
    True(smoother.TryAdd(new RgbColor(20, 20, 240), start.AddMilliseconds(100), out RgbColor next), "A sufficiently different sample should update the smoother.");
    True(next.R > 20 && next.B > 20, "Smoothed colour should move gradually between samples.");
    True(next.R < 240 && next.B < 240, "Smoothed colour should not jump directly to the new sample.");
}

static void TestForegroundWindowHistory()
{
    ForegroundWindowHistory history = new(ownProcessId: 500);
    ForegroundWindowInfo edge = new()
    {
        Handle = new IntPtr(1),
        ProcessId = 100,
        ProcessName = "msedge",
        WindowTitle = "Example page",
        WindowClass = "Chrome_WidgetWin_1",
        IsVisible = true
    };
    ForegroundWindowInfo diagnostic = new()
    {
        Handle = new IntPtr(2),
        ProcessId = 500,
        ProcessName = "ChromaTaskbar",
        WindowTitle = "Engine Diagnostic",
        WindowClass = "WindowsForms10.Window.8.app.0",
        IsVisible = true
    };

    True(history.TryUpdate(edge), "An external application should become the capture target.");
    True(!history.TryUpdate(diagnostic), "The diagnostic process must not replace the external target.");
    Equal(edge, history.LastExternal);

    ForegroundWindowInfo explorer = edge with
    {
        Handle = new IntPtr(3),
        ProcessId = 101,
        ProcessName = "explorer",
        WindowTitle = "Documents",
        WindowClass = "CabinetWClass"
    };
    True(history.TryUpdate(explorer), "A later external application should replace the prior target.");
    Equal(explorer, history.LastExternal);
}

static void TestDesktopTargetResolution()
{
    ForegroundWindowInfo ownWindow = MakeWindow(2, 500, "ChromaTaskbar", "WindowsForms10.Window.8.app.0");
    ForegroundWindowInfo desktop = MakeWindow(3, 4, "explorer", "Progman") with { IsShellWindow = true };
    ForegroundWindowInfo empty = WindowInfoResolver.Empty;

    True(ForegroundTargetResolver.Select(ownWindow, 500) is null, "ChromaTaskbar itself must not become a capture target.");
    True(ForegroundTargetResolver.Select(desktop, 500) is null, "Desktop/shell surfaces must not become capture targets.");
    True(ForegroundTargetResolver.Select(empty, 500) is null, "No foreground window means desktop mode.");

    foreach (ForegroundWindowInfo shell in new[]
    {
        MakeWindow(4, 600, "StartMenuExperienceHost", "ApplicationFrameWindow"),
        MakeWindow(5, 601, "SearchHost", "XamlExplorerHostIslandWindow"),
        MakeWindow(6, 4, "explorer", "MultitaskingViewFrame")
    })
    {
        True(ForegroundTargetResolver.Select(shell, 500) is null, $"Shell surface {shell.WindowClass} must resolve to transparency.");
    }
}

static void TestMaximizedWindowEligibility()
{
    ForegroundWindowInfo maximized = MakeWindow(7, 100, "msedge", "Chrome_WidgetWin_1", maximized: true);
    ForegroundWindowInfo backgroundMaximized = MakeWindow(10, 103, "Code", "Chrome_WidgetWin_1", maximized: true);
    ForegroundWindowInfo restored = MakeWindow(8, 101, "explorer", "CabinetWClass");
    ForegroundWindowInfo minimized = MakeWindow(9, 102, "Code", "Chrome_WidgetWin_1", maximized: true, minimized: true);

    Equal(maximized, ForegroundTargetResolver.Select(maximized, 500));
    True(backgroundMaximized.IsMaximized && ForegroundTargetResolver.Select(restored, 500) is null, "A background maximized window must not override the restored foreground window.");
    True(ForegroundTargetResolver.Select(minimized, 500) is null, "A minimized window must not remain eligible even if its previous state was maximized.");
}

static void TestForegroundSwitching()
{
    ForegroundWindowInfo edge = MakeWindow(10, 100, "msedge", "Chrome_WidgetWin_1", maximized: true);
    ForegroundWindowInfo explorer = MakeWindow(11, 101, "explorer", "CabinetWClass", maximized: true);
    ForegroundWindowInfo code = MakeWindow(12, 102, "Code", "Chrome_WidgetWin_1", maximized: true);
    Equal(edge, ForegroundTargetResolver.Select(edge, 500));
    Equal(explorer, ForegroundTargetResolver.Select(explorer, 500));
    Equal(code, ForegroundTargetResolver.Select(code, 500));
    ForegroundWindowInfo restoredEdge = edge with { IsMaximized = false };
    True(ForegroundTargetResolver.Select(restoredEdge, 500) is null, "Switching to a restored window must immediately select desktop transparency.");
}

static void TestSwitchingColors()
{
    string directory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarSwitch", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string settingsPath = Path.Combine(directory, "settings.json");
    File.WriteAllText(settingsPath, "{\"visible_window_appearance\":{\"enabled\":false},\"maximized_window_appearance\":{\"enabled\":false}}");
    try
    {
        using TranslucentTbSettingsController controller = new(settingsPath, Path.Combine(directory, "backup"), TimeSpan.Zero);
        controller.Start();
        True(controller.ApplyColor(new RgbColor(20, 90, 180)), "First application colour should be applied.");
        True(controller.ApplyColor(new RgbColor(220, 45, 35)), "Second application colour should replace the first.");
        JsonObject settings = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Equal("#DC2D23FF", settings["visible_window_appearance"]!["color"]!.GetValue<string>());
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestDesktopTransparency()
{
    string directory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarDesktop", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string settingsPath = Path.Combine(directory, "settings.json");
    File.WriteAllText(settingsPath, "{\"desktop_appearance\":{\"accent\":\"opaque\",\"color\":\"#224466FF\"},\"visible_window_appearance\":{\"enabled\":false},\"maximized_window_appearance\":{\"enabled\":false}}");
    try
    {
        using TranslucentTbSettingsController controller = new(settingsPath, Path.Combine(directory, "backup"), TimeSpan.Zero);
        controller.Start();
        controller.ApplyColor(new RgbColor(20, 90, 180));
        True(controller.SetDesktopAppearance(), "Desktop mode should turn off app-specific appearance rules.");
        JsonObject settings = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        True(!settings["visible_window_appearance"]!["enabled"]!.GetValue<bool>(), "Desktop should keep visible-window color disabled.");
        True(!settings["maximized_window_appearance"]!["enabled"]!.GetValue<bool>(), "Desktop should keep maximized-window color disabled.");
        Equal("clear", settings["desktop_appearance"]!["accent"]!.GetValue<string>());
        Equal("#00000000", settings["desktop_appearance"]!["color"]!.GetValue<string>());
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestStaleCapturePrevention()
{
    ForegroundWindowInfo captured = MakeWindow(20, 100, "msedge", "Chrome_WidgetWin_1", maximized: true);
    ForegroundWindowInfo current = MakeWindow(21, 101, "explorer", "CabinetWClass", maximized: true);
    True(!ForegroundTargetResolver.IsSameTarget(captured, current), "An older capture must be rejected after a foreground switch.");
    True(ForegroundTargetResolver.IsSameTarget(captured, captured with { WindowTitle = "Updated title" }), "A title refresh for the same HWND should remain the same target.");
}

static void TestUnchangedFrame()
{
    uint[] pixels = [0xFF204080, 0xFF204080, 0xFF204080, 0xFF204080];
    CapturedFrame first = new(2, 2, pixels, Rectangle.Empty, DateTime.UtcNow);
    CapturedFrame same = new(2, 2, pixels.ToArray(), Rectangle.Empty, DateTime.UtcNow);
    CapturedFrame changed = new(2, 2, [0xFF204080, 0xFF204080, 0xFF204080, 0xFF804020], Rectangle.Empty, DateTime.UtcNow);
    True(first.HasSamePixels(same), "Matching dimensions and pixels should be treated as unchanged.");
    True(!first.HasSamePixels(changed), "A changed pixel should invalidate the cached result.");
}

static ForegroundWindowInfo MakeWindow(
    long handle,
    uint processId,
    string name,
    string windowClass,
    bool maximized = false,
    bool minimized = false) => new()
{
    Handle = new IntPtr(handle),
    ProcessId = processId,
    ProcessName = name,
    WindowTitle = name,
    WindowClass = windowClass,
    IsVisible = true,
    IsMaximized = maximized,
    IsMinimized = minimized
};

static void TestTranslucentTbSettings()
{
    string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    string settingsPath = Path.Combine(testDirectory, "settings.json");
    string backupDirectory = Path.Combine(testDirectory, "backup");
    string original = """
        // Preserve comments and unrecognized settings in the backup.
        {
          "$schema": "https://TranslucentTB.github.io/settings.schema.json",
          "desktop_appearance": { "accent": "clear", "color": "#00000000" },
          "visible_window_appearance": { "enabled": false, "accent": "clear", "color": "#00000000", "rules": { "process_name": {} } },
          "maximized_window_appearance": { "enabled": false, "accent": "acrylic", "color": "#00000000" },
          "future_setting": { "keep": true }
        }
        """;
    byte[] originalBytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(original)];
    File.WriteAllBytes(settingsPath, originalBytes);
    bool restoredOnExit;

    try
    {
        using (TranslucentTbSettingsController controller = new(settingsPath, backupDirectory))
        {
        controller.Start();
        True(File.ReadAllBytes(controller.BackupPath!).AsSpan().SequenceEqual(originalBytes), "The original settings must be backed up byte for byte.");

        True(controller.ApplyColor(new RgbColor(20, 90, 180)), "The first active colour should be written.");
        DateTime firstWrite = File.GetLastWriteTimeUtc(settingsPath);
        True(!controller.ApplyColor(new RgbColor(20, 90, 180)), "An unchanged colour must not cause another write.");
        True(!controller.ApplyColor(new RgbColor(200, 40, 30)), "A changed colour inside the throttle interval must be deferred.");

        JsonObject activeSettings = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Equal("#145AB4FF", activeSettings["visible_window_appearance"]!["color"]!.GetValue<string>());
        Equal("opaque", activeSettings["maximized_window_appearance"]!["accent"]!.GetValue<string>());
        Equal("clear", activeSettings["desktop_appearance"]!["accent"]!.GetValue<string>());
        True(activeSettings["future_setting"]!["keep"]!.GetValue<bool>(), "Unknown properties must be preserved.");

        Thread.Sleep(TimeSpan.FromMilliseconds(1050));
        True(controller.ApplyColor(new RgbColor(200, 40, 30)), "The newest colour should be written after the throttle interval.");
        True(File.GetLastWriteTimeUtc(settingsPath) >= firstWrite, "The settings timestamp should advance after the deferred write.");

        True(!controller.SetDesktopAppearance(), "Desktop mode changes inside the throttle interval must be deferred.");
        Thread.Sleep(TimeSpan.FromMilliseconds(1050));
        True(controller.SetDesktopAppearance(), "Desktop mode should disable app-specific appearances after the throttle interval.");
        JsonObject desktopSettings = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        True(!desktopSettings["visible_window_appearance"]!["enabled"]!.GetValue<bool>(), "Visible-window appearance must be disabled on the desktop.");
        True(!desktopSettings["maximized_window_appearance"]!["enabled"]!.GetValue<bool>(), "Maximized-window appearance must be disabled on the desktop.");
        Equal("clear", desktopSettings["desktop_appearance"]!["accent"]!.GetValue<string>());

        controller.Pause();
        True(File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(originalBytes), "Pausing must restore the exact original file.");
        controller.Resume();
        True(!controller.ApplyColor(new RgbColor(20, 90, 180)), "Resume must still respect the write throttle.");
        Thread.Sleep(TimeSpan.FromMilliseconds(1050));
        True(controller.ApplyColor(new RgbColor(20, 90, 180)), "The resumed controller should apply colours again.");
        }

        restoredOnExit = File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(originalBytes);
    }
    finally
    {
        Directory.Delete(testDirectory, recursive: true);
    }

    True(restoredOnExit, "Disposal must restore the exact original file.");
}

static void TestTranslucentTbBackupFailure()
{
    string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    string settingsPath = Path.Combine(testDirectory, "settings.json");
    string backupBlockerPath = Path.Combine(testDirectory, "backup-blocker");
    byte[] originalBytes = Encoding.UTF8.GetBytes("{\"visible_window_appearance\":{\"enabled\":false}}");
    File.WriteAllBytes(settingsPath, originalBytes);
    File.WriteAllText(backupBlockerPath, "This file prevents the backup directory from being created.");

    try
    {
        using TranslucentTbSettingsController controller = new(settingsPath, backupBlockerPath);
        Throws<IOException>(controller.Start);
        True(controller.IsInitialized, "Valid settings should remain parsed when backup creation fails.");
        True(!controller.IsReady, "The controller must not be writable without a backup.");
        True(File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(originalBytes), "Backup failure must not alter settings.");
        Throws<InvalidOperationException>(() => controller.ApplyColor(new RgbColor(30, 60, 90)));
        Throws<InvalidOperationException>(() => controller.SetDesktopAppearance());
    }
    finally
    {
        Directory.Delete(testDirectory, recursive: true);
    }
}

static void TestTranslucentTbSettingsInitializationFailure()
{
    string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    string settingsPath = Path.Combine(testDirectory, "settings.json");
    string backupDirectory = Path.Combine(testDirectory, "backup");
    byte[] malformedSettings = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{ invalid")];
    File.WriteAllBytes(settingsPath, malformedSettings);

    try
    {
        using TranslucentTbSettingsController controller = new(settingsPath, backupDirectory);
        Throws<System.Text.Json.JsonException>(controller.Start);
        True(!controller.IsInitialized && !controller.IsReady, "Malformed settings must leave the controller uninitialized.");
        True(!Directory.Exists(backupDirectory), "Backup creation must not precede successful settings parsing.");
        True(File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(malformedSettings), "Parse failure must leave the settings file untouched.");
        Throws<InvalidOperationException>(() => controller.SetDesktopAppearance());
    }
    finally
    {
        Directory.Delete(testDirectory, recursive: true);
    }
}

static void TestTranslucentTbSuccessfulInitialization()
{
    string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    string settingsPath = Path.Combine(testDirectory, "settings.json");
    string backupDirectory = Path.Combine(testDirectory, "backup");
    byte[] originalBytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(
        "{\"desktop_appearance\":{\"accent\":\"clear\",\"color\":\"#00000000\"},\"visible_window_appearance\":{\"enabled\":false},\"maximized_window_appearance\":{\"enabled\":false}}")];
    File.WriteAllBytes(settingsPath, originalBytes);

    try
    {
        using TranslucentTbSettingsController controller = new(settingsPath, backupDirectory);
        controller.Start();
        True(controller.IsInitialized && controller.IsReady, "Parsed settings and a successful backup should make the controller ready.");
        True(File.Exists(controller.BackupPath), "Successful startup must leave a backup file.");
        True(File.ReadAllBytes(controller.BackupPath!).AsSpan().SequenceEqual(originalBytes), "The backup must be an exact copy.");
        True(!controller.SetDesktopAppearance(), "Matching desktop state should not write settings.");
        True(File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(originalBytes), "Initialization alone must not modify settings.");
    }
    finally
    {
        Directory.Delete(testDirectory, recursive: true);
    }
}

static void TestTaskbarStartupRetry()
{
    string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    string settingsPath = Path.Combine(testDirectory, "settings.json");
    string backupDirectory = Path.Combine(testDirectory, "backup");
    byte[] invalidBytes = Encoding.UTF8.GetBytes("{ invalid");
    byte[] validBytes = Encoding.UTF8.GetBytes(
        "{\"desktop_appearance\":{\"accent\":\"clear\",\"color\":\"#00000000\"},\"visible_window_appearance\":{\"enabled\":false},\"maximized_window_appearance\":{\"enabled\":false}}");
    try
    {
        using TranslucentTbSettingsController controller = new(settingsPath, backupDirectory);
        TaskbarStartupCoordinator retry = new(controller);
        DateTime start = DateTime.UtcNow;

        True(!retry.TryStart(start, out Exception? firstFailure), "A settings file not yet created by a late TranslucentTB startup should be retried.");
        True(firstFailure is FileNotFoundException, "The file-not-found cause should be available for diagnostics.");
        Equal(1, retry.Attempts);
        True(!controller.IsReady, "A failed initialization must remain unwritable.");

        File.WriteAllBytes(settingsPath, invalidBytes);
        True(!retry.TryStart(start.AddSeconds(1), out Exception? secondFailure), "A malformed settings file should fail safely on retry.");
        True(secondFailure is System.Text.Json.JsonException, "The parse exception should be available for diagnostics.");
        Equal(2, retry.Attempts);

        True(!retry.TryStart(start.AddMilliseconds(1500), out Exception? earlyFailure), "Retry should observe its increasing backoff interval.");
        True(earlyFailure is null, "A deferred retry is not a new initialization failure.");
        Equal(2, retry.Attempts);

        File.WriteAllBytes(settingsPath, validBytes);
        True(retry.TryStart(start.AddSeconds(3), out Exception? recoveredFailure), "Initialization should recover once settings become valid.");
        True(recoveredFailure is null, "Successful retry should clear its failure result.");
        True(controller.IsReady, "Recovered initialization must create its required backup.");
        True(File.ReadAllBytes(controller.BackupPath!).AsSpan().SequenceEqual(validBytes), "The delayed-start backup must preserve exact settings bytes.");
        Equal(3, retry.Attempts);
    }
    finally
    {
        Directory.Delete(testDirectory, recursive: true);
    }
}

static void TestStartupDiagnosticLog()
{
    string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testDirectory);
    string logPath = Path.Combine(testDirectory, "Logs", "startup.log");

    try
    {
        StartupDiagnosticLog log = new(logPath);
        log.Write("startup.begin", "attempt=1");
        log.Write("ttb.initialize.failed", "FileNotFoundException: simulated settings delay");

        string contents = File.ReadAllText(logPath);
        True(contents.Contains("[startup.begin] attempt=1", StringComparison.Ordinal), "The log should record startup events.");
        True(contents.Contains("[ttb.initialize.failed] FileNotFoundException", StringComparison.Ordinal), "The log should record initialization failures.");
        Equal(2, contents.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }
    finally
    {
        Directory.Delete(testDirectory, recursive: true);
    }
}

static void TestSingleInstanceLease()
{
    string name = $"Local\\ChromaTaskbar.Tests.{Guid.NewGuid():N}";
    True(SingleInstanceLease.TryAcquire(name, out SingleInstanceLease? first), "First process should acquire the instance lease.");
    using (first!)
    {
        bool duplicateAcquired = true;
        Thread competingThread = new(() =>
        {
            duplicateAcquired = SingleInstanceLease.TryAcquire(name, out SingleInstanceLease? duplicate);
            duplicate?.Dispose();
        });
        competingThread.Start();
        competingThread.Join();
        True(!duplicateAcquired, "A competing launch must be rejected while the first instance owns the lease.");
    }

    True(SingleInstanceLease.TryAcquire(name, out SingleInstanceLease? afterRelease), "The instance lease should be available after shutdown.");
    afterRelease!.Dispose();
}

static void TestTrayLifecycle()
{
    RunSta(() =>
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        string settingsPath = Path.Combine(testDirectory, "settings.json");
        string backupDirectory = Path.Combine(testDirectory, "backup");
        byte[] originalBytes = Encoding.UTF8.GetBytes(
            "{\"desktop_appearance\":{\"accent\":\"clear\",\"color\":\"#00000000\"},\"visible_window_appearance\":{\"enabled\":false},\"maximized_window_appearance\":{\"enabled\":false}}");
        File.WriteAllBytes(settingsPath, originalBytes);

        try
        {
            TranslucentTbSettingsController controller = new(settingsPath, backupDirectory, TimeSpan.Zero);
            StartupDiagnosticLog log = new(Path.Combine(testDirectory, "Logs", "startup.log"));
            DiagnosticForm settings = new(controller, log);
            using ChromaTaskbarApplicationContext context = new(settings);

            True(context.IsTrayIconVisible, "The tray icon should be initialized and visible.");
            True(!context.IsSettingsVisible, "The application must start without displaying its settings window.");
            True(context.IsProcessing, "Foreground monitoring and colour processing should run in the background.");
            True(settings.StartupError is null, "Normal settings initialization should finish without a startup error.");

            context.OpenSettings();
            True(context.IsSettingsVisible, "The tray Open Settings command should show the existing settings window.");
            settings.Close();
            True(!context.IsSettingsVisible && !settings.IsDisposed, "Closing settings should hide it without ending the process.");
            True(context.IsProcessing, "Closing settings must leave background processing active.");

            context.TogglePause();
            True(context.IsPaused && !context.IsProcessing, "Pause should stop foreground polling and colour processing.");
            context.TogglePause();
            True(!context.IsPaused && context.IsProcessing, "Resume should restart foreground polling and processing.");

            True(controller.ApplyColor(new RgbColor(20, 80, 160)), "The test settings should accept a colour before shutdown.");
            context.ExitApplication();
            True(settings.IsDisposed, "Explicit Exit should close the settings form and shut down the engine.");
            True(!context.IsTrayIconVisible, "Explicit Exit should dispose the notification-area icon.");
            True(File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(originalBytes), "Explicit Exit must restore the exact TranslucentTB settings bytes.");
            string logContents = File.ReadAllText(log.Path);
            True(logContents.Contains("[ttb.initialize.ready]", StringComparison.Ordinal), "Normal startup should record TranslucentTB readiness.");
            True(logContents.Contains("[shutdown.ttb.restore.completed]", StringComparison.Ordinal), "Shutdown restoration should be recorded.");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    });
}

static void TestStartupRegistration()
{
    RunSta(() =>
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "ChromaTaskbarStartupTests", Guid.NewGuid().ToString("N"));
        string startupFolder = Path.Combine(testDirectory, "Startup");
        Directory.CreateDirectory(startupFolder);
        string launcher = Path.Combine(testDirectory, "Run ChromaTaskbar.cmd");
        string commandPrompt = Path.Combine(testDirectory, "cmd.exe");
        string icon = Path.Combine(testDirectory, "ChromaTaskbar.exe");
        File.WriteAllText(launcher, "@echo off");
        try
        {
            StartupRegistrationController startup = new(startupFolder, launcher, commandPrompt, icon);
            True(!startup.IsEnabled, "Startup should initially be disabled in an empty test profile.");
            startup.SetEnabled(true);
            True(startup.IsEnabled, "Enabling should create a recognizable login shortcut.");
            startup.SetEnabled(true);
            Equal(1, Directory.GetFiles(startupFolder, "*.lnk").Length);
            True(startup.IsEnabled, "Repeated enable should not create duplicate entries.");
            startup.SetEnabled(false);
            True(!startup.IsEnabled, "Disabling should remove the app's login entry.");
            Equal(0, Directory.GetFiles(startupFolder, "*.lnk").Length);

            startup.SetEnabled(true);
            string movedInstall = Path.Combine(testDirectory, "new-install");
            Directory.CreateDirectory(movedInstall);
            string movedLauncher = Path.Combine(movedInstall, "Run ChromaTaskbar.cmd");
            File.WriteAllText(movedLauncher, "@echo off");
            StartupRegistrationController movedInstallStartup = new(startupFolder, movedLauncher, commandPrompt, icon);
            movedInstallStartup.SetEnabled(true);
            True(movedInstallStartup.IsEnabled, "A repeated install to a new folder should update the existing ChromaTaskbar entry.");
            Equal(1, Directory.GetFiles(startupFolder, "*.lnk").Length);
            movedInstallStartup.SetEnabled(false);

            startup.SetEnabled(true);
            string otherLauncher = Path.Combine(testDirectory, "OtherApp.cmd");
            File.WriteAllText(otherLauncher, "@echo off");
            StartupRegistrationController otherApp = new(startupFolder, otherLauncher, commandPrompt, icon);
            True(!otherApp.IsEnabled, "A shortcut for a different launcher must not be treated as ChromaTaskbar's entry.");
            bool protectedCollision = false;
            try { otherApp.SetEnabled(false); }
            catch (IOException) { protectedCollision = true; }
            True(protectedCollision && File.Exists(startup.ShortcutPath), "Disabling must preserve a foreign same-name shortcut.");
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, recursive: true);
        }
    });
}

static void RunSta(Action action)
{
    Exception? failure = null;
    Thread thread = new(() =>
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null)
    {
        throw new InvalidOperationException($"STA UI test failed: {failure}", failure);
    }
}

static void TestRectangleExclusion()
{
    Rectangle source = new(0, 0, 100, 100);
    Rectangle excluded = new(0, 80, 100, 20);
    Rectangle result = RectangleUtils.Subtract(source, excluded);
    True(result.Width > 0 && result.Height > 0, "Exclusion should leave a usable rectangle.");
    True(!result.IntersectsWith(excluded), "Result must not overlap the excluded taskbar.");
}

static void TestFrameValidation()
{
    try
    {
        _ = new CapturedFrame(2, 2, [0xFFFFFFFF], Rectangle.Empty, DateTime.UtcNow);
        throw new InvalidOperationException("Expected the invalid pixel count to be rejected.");
    }
    catch (ArgumentException)
    {
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static TException Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
}
