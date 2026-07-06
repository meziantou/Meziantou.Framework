namespace Meziantou.Framework.Yaml;

/// <summary>Defines the I Look Ahead Buffer contract.</summary>
public interface ILookAheadBuffer
{
    /// <summary>Gets a value indicating whether the end of the input reader has been reached.</summary>
    bool EndOfInput { get; }

    /// <summary>Gets the character at thhe specified offset.</summary>
    char Peek(int offset);

    /// <summary>
    /// Skips the next <paramref name="length"/> characters. Those characters must have been
    /// obtained first by calling the <see cref="Peek"/> method.
    /// </summary>
    void Skip(int length);

    /// <summary>
    /// Reads characters until at least <paramref name="length"/> characters are in the buffer.
    /// </summary>
    /// <param name="length">Number of characters to cache.</param>
    void Cache(int length);
}