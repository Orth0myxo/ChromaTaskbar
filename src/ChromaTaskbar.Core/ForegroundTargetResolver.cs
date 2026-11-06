namespace ChromaTaskbar.Core;

/// <summary>Chooses an eligible application window, or null for desktop/shell surfaces.</summary>
public static class ForegroundTargetResolver
{
    public static ForegroundWindowInfo? Select(
        ForegroundWindowInfo foreground,
        uint ownProcessId)
    {
        ArgumentNullException.ThrowIfNull(foreground);

        // Only the actual foreground maximized application may control the taskbar.
        // Never substitute a previously foreground app while this diagnostic or
        // another shell surface is active.
        if (foreground.IsOwnProcess(ownProcessId) || !IsEligible(foreground))
        {
            return null;
        }

        return foreground;
    }

    public static bool IsSameTarget(ForegroundWindowInfo expected, ForegroundWindowInfo? current) =>
        current is not null &&
        expected.Handle == current.Handle &&
        expected.ProcessId == current.ProcessId &&
        expected.EffectiveProcessId == current.EffectiveProcessId &&
        expected.IsMaximized == current.IsMaximized &&
        string.Equals(expected.WindowClass, current.WindowClass, StringComparison.OrdinalIgnoreCase);

    private static bool IsEligible(ForegroundWindowInfo info) =>
        info.HasWindow &&
        info.IsVisible &&
        !info.IsMinimized &&
        info.IsMaximized &&
        !info.IsCloaked &&
        !info.IsShellWindow &&
        !ShellWindowClassifier.IsShellSurface(info);
}
