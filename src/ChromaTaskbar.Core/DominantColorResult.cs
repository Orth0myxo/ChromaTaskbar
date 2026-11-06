namespace ChromaTaskbar.Core;

public sealed record DominantColorResult(
    RgbColor Rgb,
    string Hex,
    double Confidence,
    int ConsideredPixels,
    int TotalPixels,
    double Saturation,
    double Value,
    bool IsFallback);
