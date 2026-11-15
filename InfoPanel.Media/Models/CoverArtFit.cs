namespace InfoPanel.Media.Models;

public enum CoverArtFit
{
    /// <summary>Serve the thumbnail as-is (non-square art stretches in a square image element).</summary>
    Original = 0,

    /// <summary>Center-crop to a square, filling the frame.</summary>
    Crop = 1,

    /// <summary>Pad to a square with transparent bars, keeping the whole image visible.</summary>
    Fit = 2
}
