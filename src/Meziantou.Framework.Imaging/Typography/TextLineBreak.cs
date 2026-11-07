namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How strict the rules that keep some characters of Chinese and Japanese text off the start or the end of a line are.</summary>
public enum TextLineBreak
{
    /// <summary>The default rules of the Unicode line breaking algorithm.</summary>
    Normal = 0,

    /// <summary>The strictest rules: small kana and the prolonged sound mark do not start a line.</summary>
    Strict = 1,

    /// <summary>A line can be broken before or after any character that is not attached to its neighbor.</summary>
    Anywhere = 2,
}
