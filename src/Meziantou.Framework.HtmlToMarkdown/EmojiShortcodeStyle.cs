namespace Meziantou.Framework;

/// <summary>Specifies the naming style used when replacing emoji with shortcodes.</summary>
public enum EmojiShortcodeStyle
{
    /// <summary>Use GitHub-style shortcodes (for example, <c>:heart:</c>).</summary>
    GitHub,

    /// <summary>Use Unicode-style shortcodes derived from Unicode emoji names (for example, <c>:red_heart:</c>).</summary>
    Unicode,
}
