using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Colors;
using Meziantou.Framework.Pdf.Rendering.Fonts;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>The graphics state (ISO 32000-2, 8.4): the parameters the painting operators use, saved and restored by <c>q</c> and <c>Q</c>.</summary>
internal sealed class RenderGraphicsState
{
    /// <summary>Gets or sets the current transformation matrix, from user space to device space (the pixels of the image).</summary>
    public PdfMatrix Ctm { get; set; }

    public ClipPath? Clip { get; set; }

    public RenderColorSpace StrokeColorSpace { get; set; } = RenderColorSpace.Gray;

    public double[] StrokeColor { get; set; } = [0];

    public PdfObject? StrokePattern { get; set; }

    public RenderColorSpace FillColorSpace { get; set; } = RenderColorSpace.Gray;

    public double[] FillColor { get; set; } = [0];

    public PdfObject? FillPattern { get; set; }

    public double LineWidth { get; set; } = 1;

    public LineCap LineCap { get; set; }

    public LineJoin LineJoin { get; set; }

    public double MiterLimit { get; set; } = 10;

    public double[] DashArray { get; set; } = [];

    public double DashPhase { get; set; }

    public double StrokeAlpha { get; set; } = 1;

    public double FillAlpha { get; set; } = 1;

    public BlendMode BlendMode { get; set; }

    /// <summary>Gets or sets the soft mask: its dictionary and the matrix that was current when it was set (11.6.5.2).</summary>
    public (PdfDictionary Mask, PdfMatrix Matrix)? SoftMask { get; set; }

    /// <summary>Gets or sets the transfer functions of the color components, applied to the colors painted (10.5); <see langword="null"/> for none.</summary>
    public ComponentTransferPrimitive? Transfer { get; set; }

    public RenderFont? Font { get; set; }

    public double FontSize { get; set; }

    public double CharacterSpacing { get; set; }

    public double WordSpacing { get; set; }

    public double HorizontalScaling { get; set; } = 1;

    public double Leading { get; set; }

    public int TextRenderingMode { get; set; }

    public double TextRise { get; set; }

    public RenderGraphicsState Clone() => (RenderGraphicsState)MemberwiseClone();
}
