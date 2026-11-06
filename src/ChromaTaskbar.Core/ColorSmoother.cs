namespace ChromaTaskbar.Core;

public sealed class ColorSmoother
{
    private readonly TimeSpan _minInterval;
    private readonly TimeSpan _timeConstant;
    private readonly double _deltaEThreshold;
    private readonly double _minAlpha;
    private readonly double _maxAlpha;

    private LabColor _currentLab;
    private RgbColor _current;
    private DateTime? _lastTimestamp;
    private bool _hasCurrent;

    public ColorSmoother(
        TimeSpan? minInterval = null,
        TimeSpan? timeConstant = null,
        double deltaEThreshold = 2.5,
        double minAlpha = 0.05,
        double maxAlpha = 0.6)
    {
        _minInterval = minInterval ?? TimeSpan.FromMilliseconds(200);
        _timeConstant = timeConstant ?? TimeSpan.FromMilliseconds(350);
        _deltaEThreshold = Math.Max(0.0, deltaEThreshold);
        _minAlpha = Math.Clamp(minAlpha, 0.0, 1.0);
        _maxAlpha = Math.Clamp(maxAlpha, _minAlpha, 1.0);
    }

    public RgbColor? Current => _hasCurrent ? _current : null;

    public bool TryAdd(RgbColor sample, DateTime timestamp, out RgbColor output)
    {
        output = default;
        LabColor sampleLab = ColorMath.RgbToLab(sample);

        if (!_hasCurrent)
        {
            _hasCurrent = true;
            _current = sample;
            _currentLab = sampleLab;
            _lastTimestamp = timestamp;
            output = sample;
            return true;
        }

        if (_lastTimestamp is null)
        {
            _lastTimestamp = timestamp;
            return false;
        }

        TimeSpan elapsed = timestamp - _lastTimestamp.Value;
        if (elapsed < _minInterval)
        {
            return false;
        }

        if (ColorMath.DeltaE76(_currentLab, sampleLab) < _deltaEThreshold)
        {
            _lastTimestamp = timestamp;
            return false;
        }

        double seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        double alpha = 1.0 - Math.Exp(-seconds / Math.Max(_timeConstant.TotalSeconds, 0.001));
        alpha = Math.Clamp(alpha, _minAlpha, _maxAlpha);

        LabColor nextLab = ColorMath.Lerp(_currentLab, sampleLab, alpha);
        if (ColorMath.DeltaE76(_currentLab, nextLab) < 0.5)
        {
            _lastTimestamp = timestamp;
            return false;
        }

        _currentLab = nextLab;
        _current = ColorMath.LabToRgb(nextLab);
        _lastTimestamp = timestamp;
        output = _current;
        return true;
    }

    public void Reset()
    {
        _hasCurrent = false;
        _current = default;
        _currentLab = default;
        _lastTimestamp = null;
    }
}
