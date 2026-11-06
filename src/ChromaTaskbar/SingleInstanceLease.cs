namespace ChromaTaskbar;

internal sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private SingleInstanceLease(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    internal static bool TryAcquire(string name, out SingleInstanceLease? lease)
    {
        Mutex mutex = new(initiallyOwned: false, name);
        try
        {
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                lease = null;
                return false;
            }

            lease = new SingleInstanceLease(mutex);
            return true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
