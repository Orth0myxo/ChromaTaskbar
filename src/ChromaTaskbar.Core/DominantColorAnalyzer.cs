using System.Numerics;

namespace ChromaTaskbar.Core;

/// <summary>
/// Finds a broad, spatially supported colour region in a captured application window.
/// Pixels compete by area; saturation is not used as a vote multiplier.
/// </summary>
public sealed class DominantColorAnalyzer
{
    private const int Bits = 4;
    private const int Size = 1 << Bits;
    private const int BinCount = Size * Size * Size;
    private const int Mask = Size - 1;
    private const int SpatialTilesPerAxis = 8;
    private const byte MinAlpha = 128;
    private static readonly RgbColor DefaultColor = new(32, 32, 32);

    public DominantColorResult Analyze(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return AnalyzePixels(frame.PixelSpan, frame.Width, frame.Height);
    }

    public DominantColorResult Analyze(ReadOnlySpan<uint> pixels) =>
        AnalyzePixels(pixels, Math.Max(1, pixels.Length), 1);

    private static DominantColorResult AnalyzePixels(ReadOnlySpan<uint> pixels, int width, int height)
    {
        int[] counts = new int[BinCount];
        long[] sumR = new long[BinCount];
        long[] sumG = new long[BinCount];
        long[] sumB = new long[BinCount];
        ulong[] tileMasks = new ulong[BinCount];
        int totalPixels = 0;

        for (int index = 0; index < pixels.Length; index++)
        {
            uint pixel = pixels[index];
            if ((byte)(pixel >> 24) < MinAlpha)
            {
                continue;
            }

            byte r = (byte)(pixel >> 16);
            byte g = (byte)(pixel >> 8);
            byte b = (byte)pixel;
            int x = index % width;
            int y = index / width;
            int tileX = Math.Min(SpatialTilesPerAxis - 1, x * SpatialTilesPerAxis / width);
            int tileY = Math.Min(SpatialTilesPerAxis - 1, y * SpatialTilesPerAxis / height);
            int tile = tileY * SpatialTilesPerAxis + tileX;
            int bin = GetBinIndex(r, g, b);

            counts[bin]++;
            sumR[bin] += r;
            sumG[bin] += g;
            sumB[bin] += b;
            tileMasks[bin] |= 1UL << tile;
            totalPixels++;
        }

        if (totalPixels == 0)
        {
            HslColor defaultHsl = ColorMath.RgbToHsl(DefaultColor);
            return CreateResult(DefaultColor, defaultHsl, 0.0, 0, 0, isFallback: true);
        }

        BinSummary best = default;
        int bestTileSupport = -1;
        for (int bin = 0; bin < BinCount; bin++)
        {
            if (counts[bin] == 0)
            {
                continue;
            }

            int redBin = bin & Mask;
            int greenBin = (bin >> Bits) & Mask;
            int blueBin = (bin >> (Bits * 2)) & Mask;
            int count = 0;
            long r = 0;
            long g = 0;
            long b = 0;
            ulong supportedTiles = 0;

            for (int dr = -1; dr <= 1; dr++)
            {
                int nr = redBin + dr;
                if ((uint)nr >= Size) continue;
                for (int dg = -1; dg <= 1; dg++)
                {
                    int ng = greenBin + dg;
                    if ((uint)ng >= Size) continue;
                    for (int db = -1; db <= 1; db++)
                    {
                        int nb = blueBin + db;
                        if ((uint)nb >= Size) continue;
                        int neighbor = nr | (ng << Bits) | (nb << (Bits * 2));
                        count += counts[neighbor];
                        r += sumR[neighbor];
                        g += sumG[neighbor];
                        b += sumB[neighbor];
                        supportedTiles |= tileMasks[neighbor];
                    }
                }
            }

            if (count == 0)
            {
                continue;
            }

            // Pixel area is the primary vote. If similarly sized regions compete,
            // prefer the one found across more spatial tiles over a localized accent.
            int tileSupport = BitOperations.PopCount(supportedTiles);
            if (count > best.Count || (count == best.Count && tileSupport > bestTileSupport))
            {
                best = new BinSummary(count, r, g, b);
                bestTileSupport = tileSupport;
            }
        }

        RgbColor rgb = best.ToAverageColor();
        HslColor hsl = ColorMath.RgbToHsl(rgb);
        double confidence = Math.Clamp(best.Count / (double)totalPixels, 0.0, 1.0);
        return CreateResult(
            rgb,
            hsl,
            confidence,
            totalPixels,
            totalPixels,
            isFallback: hsl.Saturation < 0.12);
    }

    private static DominantColorResult CreateResult(
        RgbColor rgb,
        HslColor hsl,
        double confidence,
        int consideredPixels,
        int totalPixels,
        bool isFallback) =>
        new(
            rgb,
            rgb.ToHex(),
            confidence,
            consideredPixels,
            totalPixels,
            hsl.Saturation,
            ColorMath.MaxChannel(rgb) / 255.0,
            isFallback);

    private static int GetBinIndex(byte r, byte g, byte b) =>
        (r >> (8 - Bits)) | ((g >> (8 - Bits)) << Bits) | ((b >> (8 - Bits)) << (Bits * 2));

    private readonly record struct BinSummary(int Count, long R, long G, long B)
    {
        public RgbColor ToAverageColor() =>
            new((byte)(R / Count), (byte)(G / Count), (byte)(B / Count));
    }
}
