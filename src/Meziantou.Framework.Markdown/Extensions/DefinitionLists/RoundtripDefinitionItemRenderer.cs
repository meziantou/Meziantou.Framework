using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.DefinitionLists;

/// <summary>
/// A roundtrip renderer for a <see cref="DefinitionItem"/>: its terms, then its definition after the opening character.
/// </summary>
internal sealed class RoundtripDefinitionItemRenderer : RoundtripObjectRenderer<DefinitionItem>
{
    protected override void Write(RoundtripRenderer renderer, DefinitionItem obj)
    {
        var index = 0;
        for (; index < obj.Count && obj[index] is DefinitionTerm term; index++)
        {
            renderer.Write(term);
        }

        renderer.RenderLinesBefore(obj);

        // The opening character starts the first line of the definition, whose next lines keep their own indent
        var openingCharacter = obj.OpeningCharacter is ':' or '~' ? obj.OpeningCharacter : ':';
        var triviaAfter = obj.HasOpeningCharacterTrivia ? "" : "   ";
        renderer.PushIndent([obj.TriviaBefore.ToString() + openingCharacter + triviaAfter]);
        if (index == obj.Count)
        {
            renderer.Write(""); // trigger writing of indent
        }

        for (; index < obj.Count; index++)
        {
            renderer.Write(obj[index]);
        }

        renderer.PopIndent();
        renderer.RenderLinesAfter(obj);
    }
}
