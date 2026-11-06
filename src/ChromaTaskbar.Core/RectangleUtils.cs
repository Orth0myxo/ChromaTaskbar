using System.Drawing;

namespace ChromaTaskbar.Core;

public static class RectangleUtils
{
    public static Rectangle Subtract(Rectangle source, Rectangle excluded) => Exclude(source, excluded);

    public static Rectangle Exclude(Rectangle source, Rectangle excluded)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return Rectangle.Empty;
        }

        Rectangle intersection = Rectangle.Intersect(source, excluded);
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return source;
        }

        Rectangle[] candidates =
        [
            Rectangle.FromLTRB(source.Left, source.Top, source.Right, intersection.Top),
            Rectangle.FromLTRB(source.Left, intersection.Bottom, source.Right, source.Bottom),
            Rectangle.FromLTRB(source.Left, source.Top, intersection.Left, source.Bottom),
            Rectangle.FromLTRB(intersection.Right, source.Top, source.Right, source.Bottom)
        ];

        Rectangle best = Rectangle.Empty;
        long bestArea = 0;

        foreach (Rectangle candidate in candidates)
        {
            if (candidate.Width <= 0 || candidate.Height <= 0)
            {
                continue;
            }

            long area = (long)candidate.Width * candidate.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = candidate;
            }
        }

        return best;
    }
}
