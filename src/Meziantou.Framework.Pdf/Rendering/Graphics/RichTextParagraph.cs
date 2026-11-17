namespace Meziantou.Framework.Pdf.Rendering.Graphics;

internal sealed class RichTextParagraph(int alignment)
{
    public int Alignment { get; } = alignment;

    public List<RichTextRun> Runs { get; } = [];
}
