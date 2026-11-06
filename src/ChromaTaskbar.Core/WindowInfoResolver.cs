using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ChromaTaskbar.Core;

public static class WindowInfoResolver
{
    public static ForegroundWindowInfo Empty { get; } = new()
    {
        Handle = IntPtr.Zero,
        ProcessId = 0,
        ContentProcessId = null,
        ProcessName = string.Empty,
        ProcessPath = null,
        WindowTitle = string.Empty,
        WindowClass = string.Empty,
        IsVisible = false,
        IsMinimized = false,
        IsMaximized = false,
        IsCloaked = false,
        IsShellWindow = true,
        Bounds = null,
        CapturedAtUtc = DateTime.UtcNow
    };

    public static ForegroundWindowInfo FromForegroundWindow() =>
        FromWindow(NativeMethods.GetForegroundWindow());

    public static ForegroundWindowInfo FromWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
        {
            return Empty;
        }

        IntPtr root = NativeMethods.GetAncestor(handle, NativeMethods.GA_ROOT);
        if (root != IntPtr.Zero)
        {
            handle = root;
        }

        uint ownerProcessId = GetProcessId(handle);
        string ownerClass = GetWindowClass(handle);
        string ownerTitle = GetWindowTitle(handle);
        (string ownerName, string? ownerPath) = GetProcessInfo(ownerProcessId);

        uint? contentProcessId = null;
        IntPtr contentHandle = IntPtr.Zero;

        bool isFrameHost = ownerClass.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) ||
                           ownerName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);

        if (isFrameHost)
        {
            (contentProcessId, contentHandle) = FindContentProcess(handle, ownerProcessId);
        }

        string processName = ownerName;
        string? processPath = ownerPath;
        string title = ownerTitle;

        if (contentProcessId is not null)
        {
            (string contentName, string? contentPath) = GetProcessInfo(contentProcessId.Value);
            if (!string.IsNullOrWhiteSpace(contentName))
            {
                processName = contentName;
                processPath = contentPath;
            }

            if (string.IsNullOrWhiteSpace(title) && contentHandle != IntPtr.Zero)
            {
                title = GetWindowTitle(contentHandle);
            }
        }

        return new ForegroundWindowInfo
        {
            Handle = handle,
            ProcessId = ownerProcessId,
            ContentProcessId = contentProcessId,
            ProcessName = processName,
            ProcessPath = processPath,
            WindowTitle = title,
            WindowClass = ownerClass,
            IsVisible = NativeMethods.IsWindowVisible(handle),
            IsMinimized = NativeMethods.IsIconic(handle),
            IsMaximized = NativeMethods.IsZoomed(handle),
            IsCloaked = IsCloaked(handle),
            IsShellWindow = ShellWindowClassifier.IsShellSurface(handle, ownerClass, processName),
            Bounds = GetBounds(handle),
            CapturedAtUtc = DateTime.UtcNow
        };
    }

    public static IReadOnlyList<ForegroundWindowInfo> EnumerateTopLevelWindows(bool visibleOnly = true)
    {
        List<ForegroundWindowInfo> windows = [];

        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (visibleOnly && !NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            ForegroundWindowInfo info = FromWindow(hwnd);
            if (info.HasWindow)
            {
                windows.Add(info);
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        return windows;
    }

    internal static string GetWindowClass(IntPtr handle)
    {
        StringBuilder buffer = new(256);
        NativeMethods.GetClassNameW(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    internal static string GetWindowTitle(IntPtr handle)
    {
        int length = NativeMethods.GetWindowTextLengthW(handle);
        int capacity = Math.Max(1024, length + 1);
        StringBuilder buffer = new(capacity);
        NativeMethods.GetWindowTextW(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    internal static uint GetProcessId(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        return processId;
    }

    internal static (string Name, string? Path) GetProcessInfo(uint processId)
    {
        if (processId == 0)
        {
            return (string.Empty, null);
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string name = process.ProcessName;

            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch
            {
                path = null;
            }

            return (name, path);
        }
        catch
        {
            return (string.Empty, null);
        }
    }

    private static (uint? ProcessId, IntPtr Handle) FindContentProcess(IntPtr parent, uint ownerProcessId)
    {
        uint? contentProcessId = null;
        IntPtr contentHandle = IntPtr.Zero;

        NativeMethods.EnumWindowsProc callback = (child, _) =>
        {
            string childClass = GetWindowClass(child);
            bool candidate =
                childClass.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase) ||
                childClass.Equals("Microsoft.UI.Content.DesktopChildSiteBridge", StringComparison.OrdinalIgnoreCase);

            if (!candidate)
            {
                return true;
            }

            uint childProcessId = GetProcessId(child);
            if (childProcessId != 0 && childProcessId != ownerProcessId)
            {
                contentProcessId = childProcessId;
                contentHandle = child;
                return false;
            }

            return true;
        };

        NativeMethods.EnumChildWindows(parent, callback, IntPtr.Zero);
        return (contentProcessId, contentHandle);
    }

    private static Rectangle? GetBounds(IntPtr handle)
    {
        int result = NativeMethods.DwmGetWindowAttributeRect(
            handle,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT rect,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (result == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            return rect.ToRectangle();
        }

        if (NativeMethods.GetWindowRect(handle, out rect) &&
            rect.Right > rect.Left &&
            rect.Bottom > rect.Top)
        {
            return rect.ToRectangle();
        }

        return null;
    }

    private static bool IsCloaked(IntPtr handle)
    {
        int result = NativeMethods.DwmGetWindowAttributeInt(
            handle,
            NativeMethods.DWMWA_CLOAKED,
            out int cloaked,
            sizeof(int));

        return result == 0 && cloaked != 0;
    }
}
