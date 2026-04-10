namespace Meziantou.Framework;

/// <summary>Specifies how emoji are converted to shortcodes.</summary>
public enum EmojiShortcodeMode
{
    /// <summary>Do not replace emoji with shortcodes.</summary>
    None,

    /// <summary>Use GitHub-style shortcodes (for example, <c>:heart:</c>).</summary>
    GitHub,

    /// <summary>Use Unicode-style shortcodes derived from Unicode emoji names (for example, <c>:red_heart:</c>).</summary>
    Unicode,
}
