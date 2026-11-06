namespace ChromaTaskbar.Core;

public static class DpiAwareness
{
    public static bool TryEnablePerMonitorV2()
    {
        try
        {
            return NativeMethods.SetProcessDpiAwarenessContext(
                NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            return false;
        }
    }
}
