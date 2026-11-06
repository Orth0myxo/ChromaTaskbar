using System.Drawing;
using System.Text;

namespace ChromaTaskbar.Core;

internal static class TaskbarRectProvider
{
    public static IReadOnlyList<Rectangle> GetVisibleTaskbarRects()
    {
        List<Rectangle> rects = [];

        IntPtr primary = NativeMethods.FindWindowW("Shell_TrayWnd", null);
        AddIfVisible(primary, rects);

        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            string className = GetClassName(hwnd);
            if (className.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                AddIfVisible(hwnd, rects);
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        return rects;
    }

    private static void AddIfVisible(IntPtr hwnd, List<Rectangle> rects)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd))
        {
            return;
        }

        if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
        {
            Rectangle rectangle = rect.ToRectangle();
            if (rectangle.Width > 0 && rectangle.Height > 0)
            {
                rects.Add(rectangle);
            }
        }
    }

    private static string GetClassName(IntPtr hwnd)
    {
        StringBuilder buffer = new(256);
        NativeMethods.GetClassNameW(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
