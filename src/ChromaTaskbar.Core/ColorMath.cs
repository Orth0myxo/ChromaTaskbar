namespace ChromaTaskbar.Core;

public readonly record struct HslColor(double Hue, double Saturation, double Lightness);

public readonly record struct LabColor(double L, double A, double B);

public static class ColorMath
{
    public static HslColor RgbToHsl(RgbColor color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double lightness = (max + min) / 2.0;

        double hue = 0.0;
        double saturation = 0.0;

        if (delta > 1e-9)
        {
            saturation = lightness > 0.5
                ? delta / (2.0 - max - min)
                : delta / (max + min);

            if (max == r)
            {
                hue = (g - b) / delta + (g < b ? 6.0 : 0.0);
            }
            else if (max == g)
            {
                hue = (b - r) / delta + 2.0;
            }
            else
            {
                hue = (r - g) / delta + 4.0;
            }

            hue *= 60.0;
        }

        return new HslColor(hue, saturation, lightness);
    }

    public static RgbColor HslToRgb(HslColor color)
    {
        double hue = ((color.Hue % 360.0) + 360.0) % 360.0;
        double saturation = Clamp01(color.Saturation);
        double lightness = Clamp01(color.Lightness);

        if (saturation <= 0.0)
        {
            byte gray = ToByte(lightness);
            return new RgbColor(gray, gray, gray);
        }

        double c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        double x = c * (1.0 - Math.Abs((hue / 60.0) % 2.0 - 1.0));
        double m = lightness - c / 2.0;

        (double r, double g, double b) = hue switch
        {
            < 60.0 => (c, x, 0.0),
            < 120.0 => (x, c, 0.0),
            < 180.0 => (0.0, c, x),
            < 240.0 => (0.0, x, c),
            < 300.0 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return new RgbColor(
            ToByte(r + m),
            ToByte(g + m),
            ToByte(b + m));
    }

    public static LabColor RgbToLab(RgbColor color)
    {
        double r = SrgbToLinear(color.R / 255.0);
        double g = SrgbToLinear(color.G / 255.0);
        double b = SrgbToLinear(color.B / 255.0);

        double x = r * 0.4124564 + g * 0.3575761 + b * 0.1804375;
        double y = r * 0.2126729 + g * 0.7151522 + b * 0.0721750;
        double z = r * 0.0193339 + g * 0.1191920 + b * 0.9503041;

        double fx = LabFunction(x / 0.95047);
        double fy = LabFunction(y / 1.00000);
        double fz = LabFunction(z / 1.08883);

        return new LabColor(
            116.0 * fy - 16.0,
            500.0 * (fx - fy),
            200.0 * (fy - fz));
    }

    public static RgbColor LabToRgb(LabColor color)
    {
        double fy = (color.L + 16.0) / 116.0;
        double fx = fy + color.A / 500.0;
        double fz = fy - color.B / 200.0;

        double x = 0.95047 * LabInverse(fx);
        double y = 1.00000 * LabInverse(fy);
        double z = 1.08883 * LabInverse(fz);

        double r = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double g = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        double b = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;

        return new RgbColor(
            ToByte(LinearToSrgb(r)),
            ToByte(LinearToSrgb(g)),
            ToByte(LinearToSrgb(b)));
    }

    public static double DeltaE76(LabColor first, LabColor second)
    {
        double dl = first.L - second.L;
        double da = first.A - second.A;
        double db = first.B - second.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    public static LabColor Lerp(LabColor from, LabColor to, double amount)
    {
        double t = Clamp01(amount);
        return new LabColor(
            from.L + (to.L - from.L) * t,
            from.A + (to.A - from.A) * t,
            from.B + (to.B - from.B) * t);
    }

    public static double RelativeLuminance(RgbColor color)
    {
        double r = SrgbToLinear(color.R / 255.0);
        double g = SrgbToLinear(color.G / 255.0);
        double b = SrgbToLinear(color.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    public static byte MaxChannel(RgbColor color) => Math.Max(color.R, Math.Max(color.G, color.B));

    public static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    private static double SrgbToLinear(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double LinearToSrgb(double value)
    {
        double clamped = Clamp01(value);
        return clamped <= 0.0031308
            ? clamped * 12.92
            : 1.055 * Math.Pow(clamped, 1.0 / 2.4) - 0.055;
    }

    private static double LabFunction(double value) =>
        value > 0.008856
            ? Math.Cbrt(value)
            : 7.787 * value + 16.0 / 116.0;

    private static double LabInverse(double value)
    {
        double cube = value * value * value;
        return cube > 0.008856
            ? cube
            : (value - 16.0 / 116.0) / 7.787;
    }

    private static byte ToByte(double value) =>
        (byte)Math.Round(Clamp01(value) * 255.0, MidpointRounding.AwayFromZero);
}
