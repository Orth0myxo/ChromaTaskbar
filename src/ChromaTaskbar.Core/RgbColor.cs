namespace ChromaTaskbar.Core;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public int ToArgb() => unchecked((int)0xFF000000) | (R << 16) | (G << 8) | B;

    public static RgbColor FromArgb(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public override string ToString() => $"RGB({R}, {G}, {B}) {ToHex()}";
}
