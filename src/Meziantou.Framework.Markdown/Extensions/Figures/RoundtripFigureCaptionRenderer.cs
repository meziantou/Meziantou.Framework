using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Figures;

/// <summary>
/// A roundtrip renderer for a <see cref="FigureCaption"/>, which is written after the fence of its <see cref="Figure"/>.
/// </summary>
internal sealed class RoundtripFigureCaptionRenderer : RoundtripObjectRenderer<FigureCaption>
{
    protected override void Write(RoundtripRenderer renderer, FigureCaption obj)
    {
        renderer.WriteLeafInline(obj);
    }
}
