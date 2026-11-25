using NetVips;

namespace LanguZe.Api.Modules.Storage.Contracts;

/// <summary>
/// How busy each part of a photo is (#73): the strength of its edges, on a small grid. A plain
/// wall or floor is near 0; shelves, text and clutter are high. The Identify question's arrow is
/// placed over the plain parts, so its number and shaft cover as little of the photo as they can.
/// </summary>
/// <param name="Columns">Grid cells across; the photo is scaled to this width.</param>
/// <param name="Rows">Grid cells down, keeping the photo's shape.</param>
/// <param name="Values">Row by row, each from 0 (flat) upward.</param>
/// <param name="AspectRatio">The photo's width over its height.</param>
public sealed record EdgeMap(int Columns, int Rows, float[] Values, double AspectRatio)
{
    /// <summary>Wide enough to tell a plain wall from a shelf of books, small enough to search quickly.</summary>
    public const int Width = 160;

    /// <summary>The prepared photo's edges, through the same libvips that prepared it.</summary>
    public static EdgeMap Of(byte[] photo)
    {
        using var image = Image.NewFromBuffer(photo, access: Enums.Access.Sequential);
        var aspect = (double)image.Width / image.Height;
        using var small = image.ThumbnailImage(Width, height: Math.Max(1, (int)Math.Round(Width / aspect)), size: Enums.Size.Force);
        using var grey = small.Colourspace(Enums.Interpretation.Bw).ExtractBand(0).Cast(Enums.BandFormat.Float);
        var luma = grey.WriteToMemory<float>();
        return FromLuma(grey.Width, grey.Height, luma, aspect);
    }

    /// <summary>
    /// The edges, or null when libvips cannot read the photo: the arrow is a help, so a photo it
    /// cannot place one on keeps its boxes alone rather than losing its words.
    /// </summary>
    public static EdgeMap? TryOf(ReadOnlyMemory<byte> photo)
    {
        try
        {
            return Of(photo.ToArray());
        }
        catch (VipsException)
        {
            return null;
        }
    }

    /// <summary>The Sobel magnitude of brightness from 0 to 255, scaled to brightness from 0 to 1.</summary>
    public static EdgeMap FromLuma(int columns, int rows, float[] luma, double aspectRatio)
    {
        float At(int x, int y) => luma[(Math.Clamp(y, 0, rows - 1) * columns) + Math.Clamp(x, 0, columns - 1)] / 255f;
        var values = new float[columns * rows];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                var gx = At(x + 1, y - 1) + (2 * At(x + 1, y)) + At(x + 1, y + 1) - At(x - 1, y - 1) - (2 * At(x - 1, y)) - At(x - 1, y + 1);
                var gy = At(x - 1, y + 1) + (2 * At(x, y + 1)) + At(x + 1, y + 1) - At(x - 1, y - 1) - (2 * At(x, y - 1)) - At(x + 1, y - 1);
                values[(y * columns) + x] = MathF.Sqrt((gx * gx) + (gy * gy));
            }
        }

        return new EdgeMap(columns, rows, values, aspectRatio);
    }

    /// <summary>
    /// The mean edge strength in a square around a spot in 0–1 photo coordinates, its half-side a
    /// share of the photo's width.
    /// </summary>
    public double Busyness(double x, double y, double radius)
    {
        var (cx, cy, r) = (x * Columns, y * Rows, Math.Max(radius * Columns, 0.5));
        int left = Math.Max(0, (int)(cx - r)), right = Math.Min(Columns - 1, (int)(cx + r));
        int top = Math.Max(0, (int)(cy - r)), bottom = Math.Min(Rows - 1, (int)(cy + r));
        double sum = 0;
        var count = 0;
        for (var row = top; row <= bottom; row++)
        {
            for (var column = left; column <= right; column++)
            {
                sum += Values[(row * Columns) + column];
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }
}
