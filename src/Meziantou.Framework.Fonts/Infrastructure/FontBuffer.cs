namespace Meziantou.Framework.Fonts;

/// <summary>A buffer rented from a <see cref="FontAllocator"/>. Disposing it returns its storage.</summary>
public abstract class FontBuffer : IDisposable
{
    /// <summary>Gets the length of the buffer, in bytes.</summary>
    public abstract int Length { get; }

    /// <summary>Gets the bytes of the buffer.</summary>
    /// <exception cref="ObjectDisposedException">The buffer was returned.</exception>
    public abstract Span<byte> Span { get; }

    /// <summary>Returns the storage of the buffer.</summary>
    public abstract void Dispose();
}
