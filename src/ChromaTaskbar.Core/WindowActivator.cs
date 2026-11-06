namespace ChromaTaskbar.Core;

public static class WindowActivator
{
    public static bool Activate(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
        {
            return false;
        }

        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        NativeMethods.BringWindowToTop(handle);
        return NativeMethods.SetForegroundWindow(handle);
    }
}
