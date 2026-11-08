namespace Meziantou.Framework.Pdf;

/// <summary>The symbol of a check box or a radio button that is on: a glyph of ZapfDingbats (the normal caption <c>/CA</c> of the appearance characteristics, ISO 32000-2, 12.5.6.19).</summary>
public enum PdfCheckBoxStyle
{
    /// <summary>A check mark (✔).</summary>
    Check,

    /// <summary>A filled circle (●).</summary>
    Circle,

    /// <summary>A cross (✘).</summary>
    Cross,

    /// <summary>A filled diamond (◆).</summary>
    Diamond,

    /// <summary>A filled square (■).</summary>
    Square,

    /// <summary>A star (★).</summary>
    Star,
}
