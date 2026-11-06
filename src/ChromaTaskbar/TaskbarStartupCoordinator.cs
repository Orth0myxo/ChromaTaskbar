using ChromaTaskbar.Core;

namespace ChromaTaskbar;

internal sealed class TaskbarStartupCoordinator
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);
    private readonly TranslucentTbSettingsController _controller;
    private TimeSpan _retryDelay = InitialRetryDelay;
    private DateTime _nextAttemptUtc = DateTime.MinValue;

    internal TaskbarStartupCoordinator(TranslucentTbSettingsController controller)
    {
        _controller = controller;
    }

    internal int Attempts { get; private set; }

    internal DateTime NextAttemptUtc => _nextAttemptUtc;

    internal bool TryStart(DateTime utcNow, out Exception? failure)
    {
        failure = null;
        if (_controller.IsReady)
        {
            return true;
        }

        if (utcNow < _nextAttemptUtc)
        {
            return false;
        }

        Attempts++;
        try
        {
            _controller.Start();
            if (_controller.IsReady)
            {
                _retryDelay = InitialRetryDelay;
                _nextAttemptUtc = DateTime.MinValue;
                return true;
            }

            failure = new InvalidOperationException("TranslucentTB settings startup returned without a usable backup.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        _nextAttemptUtc = utcNow + _retryDelay;
        _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaximumRetryDelay.Ticks));
        return false;
    }
}
