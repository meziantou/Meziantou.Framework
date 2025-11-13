namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Provides methods for scrubbing (sanitizing or filtering) snapshot text after serialization.</summary>
public abstract class Scrubber
{
    /// <summary>Scrubs the specified text by filtering or transforming it.</summary>
    /// <param name="text">The text to scrub.</param>
    /// <returns>The scrubbed text.</returns>
    public abstract string Scrub(string text);
}
