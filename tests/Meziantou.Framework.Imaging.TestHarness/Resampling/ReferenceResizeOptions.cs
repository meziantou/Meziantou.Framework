namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>The parameters of a reference resize (independent of the library types).</summary>
/// <param name="TargetWidth">The target width.</param>
/// <param name="TargetHeight">The target height.</param>
/// <param name="Mode">The resize mode.</param>
/// <param name="Kernel">The kernel.</param>
/// <param name="AnchorX">The horizontal Cover anchor: 0 (left), 1/2 (center) or 1 (right) of the overflow.</param>
/// <param name="AnchorY">The vertical Cover anchor: 0 (top), 1/2 (center) or 1 (bottom) of the overflow.</param>
/// <param name="AllowUpscaling">Whether enlargement is allowed.</param>
/// <param name="Linear">Whether colors are filtered in linear light.</param>
/// <param name="Transfer">The transfer characteristics (Rec. ITU-T H.273, table 3) whose function decodes the samples to linear light: 13 is the sRGB function of IEC 61966-2-1.</param>
public sealed record ReferenceResizeOptions(
    int TargetWidth,
    int TargetHeight,
    ReferenceResizeMode Mode = ReferenceResizeMode.Contain,
    ReferenceKernel Kernel = ReferenceKernel.CatmullRom,
    decimal AnchorX = 0.5m,
    decimal AnchorY = 0.5m,
    bool AllowUpscaling = true,
    bool Linear = false,
    int Transfer = 13)
{
    /// <inheritdoc/>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Mode} {TargetWidth}x{TargetHeight} {Kernel} anchor ({AnchorX}, {AnchorY}){(AllowUpscaling ? "" : " no-upscale")}{(Linear ? " linear" : "")}{(Transfer == 13 ? "" : $" transfer {Transfer}")}");
}
