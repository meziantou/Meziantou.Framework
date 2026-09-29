using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Emoji;

/// <summary>
/// A roundtrip renderer for an <see cref="EmojiInline"/>, which writes the shortcode or smiley instead of the emoji.
/// </summary>
internal sealed class RoundtripEmojiInlineRenderer : RoundtripObjectRenderer<EmojiInline>
{
    protected override void Write(RoundtripRenderer renderer, EmojiInline obj)
    {
        if (obj.Match is not null)
        {
            renderer.Write(obj.Match);
            return;
        }

        // An inline created without a match is written as its emoji
        renderer.Write(obj.Content);
    }
}
