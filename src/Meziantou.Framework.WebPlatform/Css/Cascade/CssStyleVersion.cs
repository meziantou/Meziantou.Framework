namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// A counter incremented when a style sheet changes through the CSSOM (rules inserted or removed, declarations or
/// selectors modified, media lists, disabled flags, adopted style sheets...). The computed styles are recomputed when
/// it changes; the changes of the DOM are tracked by the version of the document.
/// </summary>
internal static class CssStyleVersion
{
    private static int s_current;

    public static int Current => Volatile.Read(ref s_current);

    public static void Increment() => Interlocked.Increment(ref s_current);
}
