namespace ChromaTaskbar.Core;

public sealed class ForegroundWindowHistory
{
    private readonly uint _ownProcessId;
    private ForegroundWindowInfo _lastExternal = WindowInfoResolver.Empty;

    public ForegroundWindowHistory(uint? ownProcessId = null)
    {
        _ownProcessId = ownProcessId ?? (uint)Environment.ProcessId;
    }

    public ForegroundWindowInfo LastExternal => _lastExternal;

    public bool TryUpdate(ForegroundWindowInfo candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.HasWindow || candidate.IsOwnProcess(_ownProcessId) || candidate.IsShellWindow)
        {
            return false;
        }

        _lastExternal = candidate;
        return true;
    }
}
