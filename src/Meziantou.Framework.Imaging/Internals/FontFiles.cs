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
