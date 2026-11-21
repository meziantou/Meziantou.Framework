namespace Meziantou.Framework.Imaging;

/// <summary>The neighbors of a pixel that a selection grows to (see <see cref="ColorSelectionOptions.Connectivity"/>).</summary>
public enum PixelConnectivity
{
    /// <summary>The 4 pixels that share a side with the pixel: a selection does not cross a diagonal.</summary>
    Four,

    /// <summary>The 8 pixels that share a side or a corner with the pixel: a selection also crosses diagonals, such as a line one pixel wide drawn at 45 degrees.</summary>
    Eight,
}
