using System.Diagnostics;

namespace Meziantou.Framework.Fonts;

internal static class FontBufferDebug
{
    /// <summary>
    /// Fills a buffer whose content was moved to a larger one, before it is returned, in Debug builds only. A span over
    /// the old storage that was kept across the move then reads NaN and -1 instead of plausible stale values, so the
    /// tests catch it before the storage is reused by another rental.
    /// </summary>
    [Conditional("DEBUG")]
    public static void PoisonMovedStorage(FontBuffer buffer) => buffer.Span.Fill(0xFF);
}
