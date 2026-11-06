namespace ChromaTaskbar.Core;

public static class ShellWindowClassifier
{
    public static bool IsOwnProcess(uint processId) => processId != 0 && processId == (uint)Environment.ProcessId;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "ForegroundStaging",
        "Shell_InputSwitchTopLevelWindow"
    };

    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost",
        "Widgets",
        "WidgetService",
        "TextInputHost",
        "LockApp",
        "LogonUI"
    };

    public static bool IsShellSurface(ForegroundWindowInfo info) =>
        IsShellSurface(info.Handle, info.WindowClass, info.ProcessName);

    public static bool IsShellSurface(IntPtr handle, string windowClass, string processName)
    {
        if (handle == IntPtr.Zero)
        {
            return true;
        }

        if (handle == NativeMethods.GetShellWindow() || handle == NativeMethods.GetDesktopWindow())
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(windowClass) && ShellClasses.Contains(windowClass))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(processName) && ShellProcesses.Contains(processName);
    }
}
