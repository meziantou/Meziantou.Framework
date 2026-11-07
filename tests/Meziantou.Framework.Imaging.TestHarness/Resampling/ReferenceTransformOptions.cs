namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>The parameters of a reference transformation (independent of the library types).</summary>
/// <param name="Forward">The transformation, from the source to the plane of the result.</param>
/// <param name="Canvas">How the canvas is chosen.</param>
/// <param name="Kernel">The kernel.</param>
/// <param name="Background">The background color, as samples of the layout of the source (alpha included when it has one).</param>
/// <param name="CanvasWidth">The width of a <see cref="ReferenceCanvasMode.Explicit"/> canvas.</param>
/// <param name="CanvasHeight">The height of a <see cref="ReferenceCanvasMode.Explicit"/> canvas.</param>
/// <param name="Centered">Whether <paramref name="Forward"/> moves the center of the image to the origin, which goes to the center of a kept or explicit canvas.</param>
/// <param name="Linear">Whether colors are filtered in linear light.</param>
/// <param name="Transfer">The transfer characteristics (Rec. ITU-T H.273, table 3) of linear light: 13 is sRGB.</param>
public sealed record ReferenceTransformOptions(
    ReferenceMatrix Forward,
    ReferenceCanvasMode Canvas,
    ReferenceKernel Kernel,
    IReadOnlyList<int> Background,
    int CanvasWidth = 0,
    int CanvasHeight = 0,
    bool Centered = false,
    bool Linear = false,
    int Transfer = 13)
{
    /// <inheritdoc/>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Forward} {Canvas}{(Canvas == ReferenceCanvasMode.Explicit ? $" {CanvasWidth}x{CanvasHeight}" : "")}{(Centered ? " centered" : "")} {Kernel} background ({string.Join(", ", Background)}){(Linear ? " linear" : "")}{(Transfer == 13 ? "" : $" transfer {Transfer}")}");
}

