namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// The strings searched by <see cref="InlineHtmlScanCache"/>.
/// </summary>
internal enum HtmlScanTarget
{
    /// <summary>The end of a processing instruction: <c>?&gt;</c>.</summary>
    ProcessingInstructionEnd,

    /// <summary>The end of a CDATA section: <c>]]&gt;</c>.</summary>
    CDataEnd,

    /// <summary>The end of a comment: <c>--&gt;</c>.</summary>
    CommentEnd,

    /// <summary>The end of a declaration: <c>&gt;</c>.</summary>
    DeclarationEnd,

    /// <summary>A null character, which ends the character-by-character scans.</summary>
    NullCharacter,
}
