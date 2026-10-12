using System.Collections;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// The faces of a font file: every face of a font collection (<c>.ttc</c>), or the single face of another font file.
/// </summary>
/// <remarks>
/// A collection is immutable and can be used by several threads at the same time. Its faces share the bytes of the
/// file, which stay in memory as long as one of the fonts is referenced.
/// </remarks>
public sealed class FontCollection : IReadOnlyList<Font>
{
    private readonly Font[] _fonts;

    private FontCollection(FontFace[] faces)
    {
        _fonts = new Font[faces.Length];
        for (var i = 0; i < faces.Length; i++)
        {
            _fonts[i] = new Font(faces[i]);
        }
    }

    /// <summary>Gets the number of faces.</summary>
    public int Count => _fonts.Length;

    /// <summary>Gets a face.</summary>
    /// <param name="index">The index of the face, in the order of the file.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative, or is not less than <see cref="Count"/>.</exception>
    public Font this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _fonts.Length);
            return _fonts[index];
        }
    }

    /// <summary>Loads every face of a font file.</summary>
    /// <param name="path">The path of the file. The file is closed before the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The faces of the file.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="UnknownImageFormatException">The file is not a font.</exception>
    /// <exception cref="InvalidImageContentException">A font is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A font uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static FontCollection Load(string path, FontLoadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new FontCollection(FontLoader.Load(path, (options ?? FontLoadOptions.Default).Configuration));
    }

    /// <summary>Loads every face of a font file from a stream, read from its current position to its end.</summary>
    /// <param name="stream">The readable source stream. It is left open and is not retained after the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The faces of the file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="UnknownImageFormatException">The data is not a font.</exception>
    /// <exception cref="InvalidImageContentException">A font is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A font uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static FontCollection Load(Stream stream, FontLoadOptions? options = null)
    {
        Font.ValidateReadableStream(stream);
        return new FontCollection(FontLoader.Load(stream, (options ?? FontLoadOptions.Default).Configuration));
    }

    /// <summary>Loads every face of a font file from its bytes.</summary>
    /// <param name="data">The bytes of the file. They are copied: the fonts do not refer to the span after the method returns.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <returns>The faces of the file.</returns>
    /// <exception cref="UnknownImageFormatException">The data is not a font.</exception>
    /// <exception cref="InvalidImageContentException">A font is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A font uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    public static FontCollection Load(ReadOnlySpan<byte> data, FontLoadOptions? options = null)
        => new(FontLoader.Load(data, (options ?? FontLoadOptions.Default).Configuration));

    /// <summary>Asynchronously loads every face of a font file, using asynchronous file I/O.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The faces of the file.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<FontCollection> LoadAsync(string path, FontLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return LoadCoreAsync(FontLoader.LoadAsync(path, (options ?? FontLoadOptions.Default).Configuration, cancellationToken));
    }

    /// <summary>Asynchronously loads every face of a font file from a stream, read from its current position to its end.</summary>
    /// <param name="stream">The readable source stream. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="FontLoadOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The faces of the file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public static Task<FontCollection> LoadAsync(Stream stream, FontLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        Font.ValidateReadableStream(stream);
        return LoadCoreAsync(FontLoader.LoadAsync(stream, (options ?? FontLoadOptions.Default).Configuration, cancellationToken));
    }

    /// <summary>Returns an enumerator of the faces, in the order of the file.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<Font> GetEnumerator() => ((IEnumerable<Font>)_fonts).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => _fonts.GetEnumerator();

    private static async Task<FontCollection> LoadCoreAsync(Task<FontFace[]> faces) => new(await faces.ConfigureAwait(false));
}
