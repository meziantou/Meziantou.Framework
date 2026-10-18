using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The version of the style sheets of a document (<see cref="Document.StyleSheetsVersion"/>), changed when one of them changes
/// through the CSSOM (rules inserted or removed, declarations or selectors modified, media lists, disabled flags, adopted style
/// sheets...). The computed styles of the document are recomputed when it changes; the changes of the DOM are tracked by the
/// version of the document. A change of the style sheets of a document does not change the version of the other documents.
/// The versions are taken from a process-wide sequence: two documents never have the same version, so a cache that moves to
/// another document (e.g. the style sheet list of a shadow root adopted by another document) never sees an unchanged version.
/// </summary>
internal static class CssStyleVersion
{
    private static int s_last;

    /// <summary>Returns a version that was never returned before.</summary>
    public static int Next() => Interlocked.Increment(ref s_last);

    /// <summary>The style sheets of the document changed.</summary>
    public static void Increment(Document? document)
    {
        if (document is not null)
        {
            document.StyleSheetsVersion = Next();
        }
    }

    /// <summary>
    /// The style sheet changed: the version of the document it applies to is changed. A style sheet that applies to no document
    /// (e.g. a constructed style sheet that was never adopted) changes no version: its insertion in a document does.
    /// </summary>
    public static void Increment(CssStyleSheet? sheet) => Increment(sheet?.StyleDocument);

    /// <summary>The rule changed: the version of the document of its style sheet is changed.</summary>
    public static void Increment(CssRule? rule) => Increment(rule?.ParentStyleSheet);
}
