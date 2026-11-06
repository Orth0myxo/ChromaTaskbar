using ChromaTaskbar.Core;
using System.Diagnostics;
using System.Drawing;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This diagnostic probe requires Windows.");
    return 2;
}

DpiAwareness.TryEnablePerMonitorV2();
var capturer = new WindowCapturer();
var analyzer = new DominantColorAnalyzer();
var targets = new (string DisplayName, string[] ProcessNames)[]
{
    ("Microsoft Edge", ["msedge"]),
    ("File Explorer", ["explorer"]),
    ("Visual Studio Code", ["Code"])
};

IReadOnlyList<ForegroundWindowInfo> windows = WindowInfoResolver.EnumerateTopLevelWindows();
ForegroundWindowInfo foreground = WindowInfoResolver.FromForegroundWindow();
int tested = 0;
Console.WriteLine($"Foreground: {foreground.ProcessName} | {foreground.WindowTitle}");

foreach ((string displayName, string[] processNames) in targets)
{
    ForegroundWindowInfo? candidate = windows
        .Where(window => processNames.Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase))
        .Where(window => window.IsVisible && !window.IsMinimized && !window.IsCloaked && !window.IsShellWindow)
        .OrderByDescending(window => window.Handle == foreground.Handle)
        .ThenByDescending(window => window.Bounds is Rectangle bounds ? (long)bounds.Width * bounds.Height : 0)
        .FirstOrDefault();

    if (candidate is null)
    {
        bool processRunning = false;
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    processRunning |= processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                }
            }
        }
        Console.WriteLine(processRunning
            ? $"{displayName}: process is running, but no eligible visible top-level window was found; skipped."
            : $"{displayName}: process is not running; skipped.");
        continue;
    }

    tested++;
    CaptureResult result = capturer.Capture(candidate);
    if (!result.Success || result.Frame is null)
    {
        Console.WriteLine($"{displayName}: capture failed ({result.FailureReason}: {result.Message})");
        continue;
    }

    DominantColorResult color = analyzer.Analyze(result.Frame);
    Console.WriteLine($"{displayName}: capture succeeded, {result.Frame.Width}×{result.Frame.Height}, {color.Hex}, confidence {color.Confidence:P0}");
}

Console.WriteLine($"Visible target applications tested: {tested}.");
Console.WriteLine("Frames were processed in memory and were not written to disk.");
return 0;
