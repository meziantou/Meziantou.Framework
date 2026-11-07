using System.Collections;

namespace Meziantou.Framework.Imaging;

/// <summary>The read-only, ordered list of the displayed frames of an <see cref="Image"/>. Never empty.</summary>
/// <remarks>
/// The collection is a live view: it reflects structural edits made through the owning image
/// (<see cref="Image.AppendFrame(ImageFrame)"/>, <see cref="Image.RemoveFrame(int)"/>, ...). Structural edits invalidate
/// enumerators, which then throw an <see cref="InvalidOperationException"/>.
/// </remarks>
public abstract class ImageFrameCollection : IReadOnlyList<ImageFrame>
{
    private protected ImageFrameCollection()
    {
    }

    /// <summary>Gets the number of displayed frames. Always at least 1.</summary>
    public abstract int Count { get; }

    /// <summary>Gets the frame at the specified index.</summary>
    /// <param name="index">The index of the frame.</param>
    /// <returns>The borrowed frame.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public abstract ImageFrame this[int index] { get; }

    private protected abstract int Version { get; }

    /// <summary>Returns the index of a frame in this collection.</summary>
    /// <param name="frame">The frame to locate.</param>
    /// <returns>The index of the frame, or -1 if it is not a displayed frame of this image (for example the poster frame).</returns>
    public int IndexOf(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var count = Count;
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(this[i], frame))
                return i;
        }

        return -1;
    }

    /// <summary>Determines whether a frame is a displayed frame of this image.</summary>
    /// <param name="frame">The frame to locate.</param>
    /// <returns><see langword="true"/> if the frame belongs to this collection; otherwise <see langword="false"/>.</returns>
    public bool Contains(ImageFrame frame) => IndexOf(frame) >= 0;

    /// <summary>Returns an enumerator over the frames.</summary>
    /// <returns>An enumerator that throws if the collection is structurally modified during enumeration.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<ImageFrame> IEnumerable<ImageFrame>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the frames of an <see cref="ImageFrameCollection"/>.</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Standard collection enumerator pattern.")]
    public struct Enumerator : IEnumerator<ImageFrame>
    {
        private readonly ImageFrameCollection _collection;
        private readonly int _version;
        private int _index;

        internal Enumerator(ImageFrameCollection collection)
        {
            _collection = collection;
            _version = collection.Version;
            _index = -1;
        }

        /// <summary>Gets the current frame.</summary>
        public readonly ImageFrame Current => _collection[_index];

        readonly object IEnumerator.Current => Current;

        /// <summary>Advances to the next frame.</summary>
        /// <returns><see langword="true"/> if there is a current frame; <see langword="false"/> at the end of the collection.</returns>
        /// <exception cref="InvalidOperationException">The collection was structurally modified.</exception>
        public bool MoveNext()
        {
            if (_version != _collection.Version)
                throw new InvalidOperationException("The frame collection was modified; enumeration cannot continue.");

            if (_index + 1 < _collection.Count)
            {
                _index++;
                return true;
            }

            _index = _collection.Count;
            return false;
        }

        /// <summary>Resets the enumerator to its initial position.</summary>
        public void Reset() => _index = -1;

        /// <summary>Releases the enumerator. This method does nothing.</summary>
        public readonly void Dispose()
        {
        }
    }
}
