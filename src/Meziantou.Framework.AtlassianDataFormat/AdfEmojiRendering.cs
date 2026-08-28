namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How an <see cref="AdfEmoji"/> is converted.</summary>
public enum AdfEmojiRendering
{
    /// <summary>The literal characters of the emoji, falling back to its shortcode.</summary>
    Text = 0,

    /// <summary>The shortcode of the emoji, such as <c>:smile:</c>.</summary>
    ShortName,
}
