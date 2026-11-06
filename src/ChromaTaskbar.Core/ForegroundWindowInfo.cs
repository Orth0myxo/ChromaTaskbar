using System.Drawing;

namespace ChromaTaskbar.Core;

public sealed record ForegroundWindowInfo
{
    public IntPtr Handle { get; init; }
    public uint ProcessId { get; init; }
    public uint? ContentProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? ProcessPath { get; init; }
    public string WindowTitle { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;
    public bool IsVisible { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsMaximized { get; init; }
    public bool IsCloaked { get; init; }
    public bool IsShellWindow { get; init; }
    public Rectangle? Bounds { get; init; }
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    public bool HasWindow => Handle != IntPtr.Zero;

    public uint EffectiveProcessId => ContentProcessId ?? ProcessId;

    public bool IsOwnProcess(uint processId) =>
        ProcessId == processId || ContentProcessId == processId;
}
