using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using InfoPanel.Media.Models;

namespace InfoPanel.Media.Services;

/// <summary>Squares non-square thumbnails (e.g. 16:9 video art) so they don't stretch in InfoPanel.</summary>
public static class CoverArtProcessor
{
    /// <summary>Returns PNG bytes squared per <paramref name="fit"/>, or the input unchanged when no work is needed.</summary>
    public static byte[] Apply(byte[] imageBytes, CoverArtFit fit)
    {
        if (fit == CoverArtFit.Original)
            return imageBytes;

        try
        {
            using var input = new MemoryStream(imageBytes);
            using var source = new Bitmap(input);

            if (source.Width == source.Height)
                return imageBytes;

            var side = fit == CoverArtFit.Crop
                ? Math.Min(source.Width, source.Height)
                : Math.Max(source.Width, source.Height);

            using var result = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Same-size draw centered: offsets are negative for Crop (trims edges), positive for Fit (pads)
                g.DrawImage(source,
                    new Rectangle((side - source.Width) / 2, (side - source.Height) / 2, source.Width, source.Height));
            }

            using var output = new MemoryStream();
            result.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Media] {DateTime.UtcNow:o} Error applying {nameof(CoverArtFit)} {fit}: {ex.Message}");
            return imageBytes;
        }
    }
}
