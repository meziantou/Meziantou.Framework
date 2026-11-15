using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Loads the faces of a font file with the options of the library, and reports its errors with the exceptions of the library.</summary>
internal static class FontFiles
{
    public static FontFace[] Load(ReadOnlySpan<byte> data, FontLoadOptions options)
    {
        try
        {
            return FontLoader.Load(data, options.Configuration.FontEnvironment, options.UseEmbeddedBitmaps);
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }
    }

    public static FontFace LoadType1(ReadOnlySpan<byte> data, AfmFontMetrics? metrics, FontLoadOptions options)
    {
        try
        {
            return FontLoader.LoadType1(data, metrics, options.Configuration.FontEnvironment);
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }
    }

    public static FontFace LoadType1(string path, AfmFontMetrics? metrics, FontLoadOptions options)
    {
        using var stream = ImageIO.OpenFile(path, asynchronous: false);

        // The file is checked against the limit before it is read, into a buffer charged to the live allocations of
        // the configuration while the font is converted
        var limit = Math.Min(options.Configuration.Limits.MaxEncodedBytes, Array.MaxLength);
        if (stream.Length > limit)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, limit, stream.Length);

        var scope = AllocationScope.Create(options.Configuration, "Type1Font");
        using var buffer = scope.Rent((int)stream.Length, AllocationKind.Temporary, clear: false);
        var data = buffer.Memory.Span;
        stream.ReadExactly(data);
        return LoadType1(data, metrics, options);
    }

    public static FontFace[] Load(string path, FontLoadOptions options)
    {
        using var stream = ImageIO.OpenFile(path, asynchronous: false);
        return Load(stream, options);
    }

    public static FontFace[] Load(Stream stream, FontLoadOptions options)
    {
        try
        {
            return FontLoader.Load(stream, options.Configuration.FontEnvironment, options.UseEmbeddedBitmaps);
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }
    }

    public static async Task<FontFace[]> LoadAsync(string path, FontLoadOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = ImageIO.OpenFile(path, asynchronous: true);
        await using (stream.ConfigureAwait(false))
        {
            return await LoadAsync(stream, options, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<FontFace[]> LoadAsync(Stream stream, FontLoadOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await FontLoader.LoadAsync(stream, options.Configuration.FontEnvironment, options.UseEmbeddedBitmaps, cancellationToken).ConfigureAwait(false);
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }
    }
}
