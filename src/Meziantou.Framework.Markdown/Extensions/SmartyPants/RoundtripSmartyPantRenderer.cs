using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.SmartyPants;

/// <summary>
/// A roundtrip renderer for a <see cref="SmartyPant"/>, which writes the characters of the source instead of the typographic ones.
/// </summary>
internal sealed class RoundtripSmartyPantRenderer : RoundtripObjectRenderer<SmartyPant>
{
    protected override void Write(RoundtripRenderer renderer, SmartyPant obj)
    {
        renderer.Write(obj.ToString());
    }
}
