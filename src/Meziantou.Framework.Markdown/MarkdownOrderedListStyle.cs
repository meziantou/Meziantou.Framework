namespace Meziantou.Framework.Markdown;

/// <summary>How the items of an ordered list are numbered.</summary>
public enum MarkdownOrderedListStyle
{
    /// <summary>Numbers: <c>1.</c>, <c>2.</c>...</summary>
    Numbers,

    /// <summary>Lowercase letters, with <see cref="MarkdownExtensions.ListExtras"/>: <c>a.</c>, <c>b.</c>...</summary>
    LowerAlpha,

    /// <summary>Uppercase letters, with <see cref="MarkdownExtensions.ListExtras"/>: <c>A.</c>, <c>B.</c>...</summary>
    UpperAlpha,

    /// <summary>Lowercase roman numbers, with <see cref="MarkdownExtensions.ListExtras"/>: <c>i.</c>, <c>ii.</c>...</summary>
    LowerRoman,

    /// <summary>Uppercase roman numbers, with <see cref="MarkdownExtensions.ListExtras"/>: <c>I.</c>, <c>II.</c>...</summary>
    UpperRoman,
}
