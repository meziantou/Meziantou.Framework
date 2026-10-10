using System.Collections;

namespace Meziantou.Framework.Imaging;

/// <summary>The typed, read-only, ordered list of the displayed frames of an <see cref="Image{TPixel}"/>. Never empty.</summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
/// <remarks>The typed and untyped collections of an image expose the same frame objects.</remarks>
public sealed class ImageFrameCollection<TPixel> : ImageFrameCollection, IReadOnlyList<ImageFrame<TPixel>>
    where TPixel : unmanaged
{
    private readonly Image<TPixel> _image;

    internal ImageFrameCollection(Image<TPixel> image) => _image = image;

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public override int Count => _image.GetFrameList().Count;

    /// <summary>Gets the frame at the specified index.</summary>
    /// <param name="index">The index of the frame.</param>
    /// <returns>The borrowed frame.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    public override ImageFrame<TPixel> this[int index]
    {
        get
        {
            var frames = _image.GetFrameList();
            if ((uint)index >= (uint)frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, "The frame index is out of range.");

            return (ImageFrame<TPixel>)frames[index];
        }
    }

    private protected override int Version => _image.StructureVersion;

    /// <summary>Returns an enumerator over the typed frames.</summary>
    /// <returns>An enumerator that throws if the collection is structurally modified during enumeration.</returns>
    public new Enumerator GetEnumerator() => new(this);

    IEnumerator<ImageFrame<TPixel>> IEnumerable<ImageFrame<TPixel>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the frames of an <see cref="ImageFrameCollection{TPixel}"/>.</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Standard collection enumerator pattern.")]
    public new struct Enumerator : IEnumerator<ImageFrame<TPixel>>
    {
        private ImageFrameCollection.Enumerator _inner;

        internal Enumerator(ImageFrameCollection<TPixel> collection) => _inner = new ImageFrameCollection.Enumerator(collection);

        /// <summary>Gets the current frame.</summary>
        public readonly ImageFrame<TPixel> Current => (ImageFrame<TPixel>)_inner.Current;

        readonly object IEnumerator.Current => Current;

        /// <summary>Advances to the next frame.</summary>
        /// <returns><see langword="true"/> if there is a current frame; <see langword="false"/> at the end of the collection.</returns>
        /// <exception cref="InvalidOperationException">The collection was structurally modified.</exception>
        public bool MoveNext() => _inner.MoveNext();

        /// <summary>Resets the enumerator to its initial position.</summary>
        public void Reset() => _inner.Reset();

        /// <summary>Releases the enumerator. This method does nothing.</summary>
        public readonly void Dispose()
        {
        }
    }
}
