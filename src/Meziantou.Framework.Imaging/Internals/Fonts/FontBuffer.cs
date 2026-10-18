namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A buffer rented from a <see cref="FontAllocator"/>. Disposing it returns its storage.</summary>
internal abstract class FontBuffer : IDisposable
{
    /// <summary>Gets the length of the buffer, in bytes.</summary>
    public abstract int Length { get; }

    /// <summary>Gets the bytes of the buffer.</summary>
    /// <exception cref="ObjectDisposedException">The buffer was returned.</exception>
    public abstract Span<byte> Span { get; }

    public abstract void Dispose();
}
