using System.Drawing;

namespace ChromaTaskbar.Core;

public sealed class CapturedFrame
{
    private readonly uint[] _pixels;

    public CapturedFrame(
        int width,
        int height,
        uint[] pixels,
        Rectangle sourceRectangle,
        DateTime capturedAtUtc)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != width * height)
        {
            throw new ArgumentException("Pixel count does not match frame dimensions.", nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
        SourceRectangle = sourceRectangle;
        CapturedAtUtc = capturedAtUtc;
    }

    public int Width { get; }

    public int Height { get; }

    public Rectangle SourceRectangle { get; }

    public DateTime CapturedAtUtc { get; }

    public ReadOnlyMemory<uint> Pixels => _pixels;

    public ReadOnlySpan<uint> PixelSpan => _pixels;

    public bool HasSamePixels(CapturedFrame other) =>
        other is not null &&
        Width == other.Width &&
        Height == other.Height &&
        PixelSpan.SequenceEqual(other.PixelSpan);
}

public enum CaptureFailureReason
{
    None = 0,
    NoWindow,
    InvalidWindow,
    NotVisible,
    Minimized,
    Cloaked,
    ShellWindow,
    OwnProcess,
    InvalidBounds,
    OutsideScreen,
    TaskbarOverlap,
    TooLarge,
    ScreenDcFailed,
    BitBltFailed,
    EmptyCapture,
    Exception
}

public sealed record CaptureResult
{
    public bool Success { get; init; }
    public CapturedFrame? Frame { get; init; }
    public CaptureFailureReason FailureReason { get; init; }
    public string? Message { get; init; }
    public Rectangle SourceRectangle { get; init; }
    public TimeSpan Duration { get; init; }

    public static CaptureResult Ok(CapturedFrame frame, Rectangle sourceRectangle, TimeSpan duration) =>
        new()
        {
            Success = true,
            Frame = frame,
            FailureReason = CaptureFailureReason.None,
            Message = null,
            SourceRectangle = sourceRectangle,
            Duration = duration
        };

    public static CaptureResult Fail(
        CaptureFailureReason reason,
        string? message,
        Rectangle sourceRectangle,
        TimeSpan duration) =>
        new()
        {
            Success = false,
            Frame = null,
            FailureReason = reason,
            Message = message,
            SourceRectangle = sourceRectangle,
            Duration = duration
        };
}
