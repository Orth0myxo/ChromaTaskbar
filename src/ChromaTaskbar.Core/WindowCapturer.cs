using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChromaTaskbar.Core;

public sealed class WindowCapturer
{
    private const int MaxCapturePixels = 16_000_000;

    public CaptureResult Capture(ForegroundWindowInfo? info, int maxDimension = 96)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Rectangle sourceRectangle = Rectangle.Empty;

        try
        {
            if (info is null || !info.HasWindow)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.NoWindow,
                    "No window information supplied.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            if (!info.IsVisible)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.NotVisible,
                    "The window is not visible.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            if (info.IsMinimized)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.Minimized,
                    "The window is minimized.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            if (info.IsCloaked)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.Cloaked,
                    "The window is cloaked.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            if (ShellWindowClassifier.IsOwnProcess(info.ProcessId))
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.OwnProcess,
                    "Windows owned by this diagnostic process are excluded.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            if (info.IsShellWindow)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.ShellWindow,
                    "Shell and own-process windows are excluded.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            Rectangle frame = info.Bounds ?? GetFrameBounds(info.Handle);
            if (frame.Width <= 0 || frame.Height <= 0)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.InvalidBounds,
                    "The window has no valid screen bounds.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            Rectangle client = GetClientScreenRectangle(info.Handle);
            Rectangle capture;
            if (client.Width > 0 && client.Height > 0)
            {
                capture = Rectangle.Intersect(frame, client);
            }
            else
            {
                capture = frame;
            }

            if (capture.Width <= 0 || capture.Height <= 0)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.InvalidBounds,
                    "The client area and frame do not intersect.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            sourceRectangle = capture;

            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            Rectangle onScreen = Rectangle.Intersect(capture, virtualScreen);
            if (onScreen.Width <= 0 || onScreen.Height <= 0)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.OutsideScreen,
                    "The window is outside the virtual screen.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            foreach (Rectangle taskbar in TaskbarRectProvider.GetVisibleTaskbarRects())
            {
                onScreen = RectangleUtils.Subtract(onScreen, taskbar);
                if (onScreen.Width <= 0 || onScreen.Height <= 0)
                {
                    return CaptureResult.Fail(
                        CaptureFailureReason.TaskbarOverlap,
                        "The visible window area is covered by the taskbar.",
                        sourceRectangle,
                        stopwatch.Elapsed);
                }
            }

            if ((long)onScreen.Width * onScreen.Height > MaxCapturePixels)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.TooLarge,
                    "The capture region is too large.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            using Bitmap source = new(
                onScreen.Width,
                onScreen.Height,
                PixelFormat.Format32bppArgb);

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.ScreenDcFailed,
                    "GetDC failed.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            bool copied;
            try
            {
                using Graphics graphics = Graphics.FromImage(source);
                IntPtr destinationDc = graphics.GetHdc();
                try
                {
                    copied = NativeMethods.BitBlt(
                        destinationDc,
                        0,
                        0,
                        onScreen.Width,
                        onScreen.Height,
                        screenDc,
                        onScreen.X,
                        onScreen.Y,
                        NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                }
                finally
                {
                    graphics.ReleaseHdc(destinationDc);
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            if (!copied)
            {
                int error = Marshal.GetLastWin32Error();
                return CaptureResult.Fail(
                    CaptureFailureReason.BitBltFailed,
                    $"BitBlt failed with Win32 error {error}.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            using Bitmap scaled = ScaleDown(source, maxDimension);
            uint[] pixels = ReadPixels(scaled);
            if (pixels.Length == 0)
            {
                return CaptureResult.Fail(
                    CaptureFailureReason.EmptyCapture,
                    "The captured frame contains no pixels.",
                    sourceRectangle,
                    stopwatch.Elapsed);
            }

            CapturedFrame frameResult = new(
                scaled.Width,
                scaled.Height,
                pixels,
                sourceRectangle,
                DateTime.UtcNow);

            return CaptureResult.Ok(frameResult, sourceRectangle, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return CaptureResult.Fail(
                CaptureFailureReason.Exception,
                $"{ex.GetType().Name}: {ex.Message}",
                sourceRectangle,
                stopwatch.Elapsed);
        }
    }

    private static Bitmap ScaleDown(Bitmap source, int maxDimension)
    {
        int max = Math.Clamp(maxDimension, 1, 96);
        double scale = Math.Min(
            1.0,
            max / (double)Math.Max(source.Width, source.Height));

        if (scale >= 1.0)
        {
            return (Bitmap)source.Clone();
        }

        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));

        Bitmap scaled = new(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(scaled);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, width, height),
                new Rectangle(0, 0, source.Width, source.Height),
                GraphicsUnit.Pixel);

            return scaled;
        }
        catch
        {
            scaled.Dispose();
            throw;
        }
    }

    private static uint[] ReadPixels(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        uint[] pixels = new uint[width * height];

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int[] row = new int[width];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(
                    IntPtr.Add(data.Scan0, y * data.Stride),
                    row,
                    0,
                    width);

                int offset = y * width;
                for (int x = 0; x < width; x++)
                {
                    pixels[offset + x] = unchecked((uint)row[x]);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return pixels;
    }

    private static Rectangle GetFrameBounds(IntPtr handle)
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

        return Rectangle.Empty;
    }

    private static Rectangle GetClientScreenRectangle(IntPtr handle)
    {
        if (!NativeMethods.GetClientRect(handle, out NativeMethods.RECT rect))
        {
            return Rectangle.Empty;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return Rectangle.Empty;
        }

        NativeMethods.POINT origin = new() { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(handle, ref origin))
        {
            return Rectangle.Empty;
        }

        return new Rectangle(origin.X, origin.Y, width, height);
    }
}
