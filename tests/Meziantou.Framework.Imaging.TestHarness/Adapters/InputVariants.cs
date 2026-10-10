using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// Runs the public eager <c>Identify</c>/<c>Load</c> entry points over every <see cref="InputVariant"/> and checks the
/// ownership contract after each call, success or failure: caller streams are never disposed (and never read past their
/// end or before their initial position), and path overloads close their file before returning.
/// </summary>
public static class InputVariants
{
    /// <summary>Unrelated bytes placed before the data of <see cref="InputVariant.OffsetStream"/>.</summary>
    private static ReadOnlySpan<byte> OffsetPrefix => "not an image: skip me!"u8;

    /// <summary>Gets every variant.</summary>
    public static IReadOnlyList<InputVariant> All { get; } = Enum.GetValues<InputVariant>();

    /// <summary>Gets the variants of sequential readers: every variant except <see cref="InputVariant.Span"/> (readers have path and stream overloads only).</summary>
    public static IReadOnlyList<InputVariant> ReaderVariants { get; } = [.. All.Where(variant => variant != InputVariant.Span)];

    /// <summary>Gets a value indicating whether a variant uses the asynchronous overloads (its streams forbid synchronous reads).</summary>
    public static bool IsAsynchronous(InputVariant variant) => variant is InputVariant.AsyncPath or InputVariant.AsyncStream or InputVariant.AsyncShortReadStream;

    /// <summary>
    /// Opens a sequential reader over <paramref name="data"/> through a variant, runs <paramref name="consume"/> (which must use
    /// the asynchronous reader methods when its second argument is <see langword="true"/>), disposes the reader, and checks the
    /// ownership contract: caller streams stay open (default <see cref="ImageReaderOptions.LeaveOpen"/>) and are never read past
    /// their end; path readers close their file when disposed.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ConsumeAsync disposes the reader.")]
    public static async Task<T> ReadAsync<TPixel, T>(InputVariant variant, byte[] data, ImageFormat format, ImageReaderOptions? options, Func<ImageReader<TPixel>, bool, Task<T>> consume, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(consume);
        if (variant == InputVariant.Span)
            throw new ArgumentOutOfRangeException(nameof(variant), variant, "Sequential readers have no span overload.");

        var asynchronous = IsAsynchronous(variant);
        switch (variant)
        {
            case InputVariant.Path:
            case InputVariant.MisleadingExtensionPath:
            case InputVariant.AsyncPath:
            {
                var extension = variant == InputVariant.MisleadingExtensionPath ? GetMisleadingExtension(format) : GetExtension(format);
                var path = WriteTemporaryFile(data, extension);
                try
                {
                    try
                    {
                        var reader = asynchronous ? await Image.OpenReaderAsync<TPixel>(path, options, cancellationToken).ConfigureAwait(false) : Image.OpenReader<TPixel>(path, options);
                        return await ConsumeAsync(reader, asynchronous, consume).ConfigureAwait(false);
                    }
                    finally
                    {
                        EnsureFileClosed(path);
                    }
                }
                finally
                {
                    File.Delete(path);
                }
            }

            default:
            {
                using var stream = CreateStream(variant, data);
                try
                {
                    var reader = asynchronous ? await Image.OpenReaderAsync<TPixel>(stream, options, cancellationToken).ConfigureAwait(false) : Image.OpenReader<TPixel>(stream, options);
                    return await ConsumeAsync(reader, asynchronous, consume).ConfigureAwait(false);
                }
                finally
                {
                    EnsureStreamContract(variant, stream, data.Length);
                }
            }
        }
    }

    /// <summary>Gets a file extension that names another format than <paramref name="format"/>.</summary>
    public static string GetMisleadingExtension(ImageFormat format) => format switch
    {
        ImageFormat.Png => ".gif",
        ImageFormat.Gif => ".jpg",
        _ => ".png",
    };

    /// <summary>Gets the usual file extension of a format.</summary>
    public static string GetExtension(ImageFormat format) => format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Gif => ".gif",
        ImageFormat.Jpeg => ".jpg",
        _ => ".bin",
    };

    /// <summary>Identifies <paramref name="data"/> through a variant.</summary>
    public static Task<ImageInfo> IdentifyAsync(InputVariant variant, byte[] data, ImageFormat format, ImageIdentifyOptions? options, CancellationToken cancellationToken = default)
        => RunAsync(
            variant,
            data,
            format,
            bytes => Image.Identify(bytes, options),
            path => Image.Identify(path, options),
            stream => Image.Identify(stream, options),
            (path, token) => Image.IdentifyAsync(path, options, token),
            (stream, token) => Image.IdentifyAsync(stream, options, token),
            cancellationToken);

    /// <summary>Loads <paramref name="data"/> in its default pixel format through a variant.</summary>
    public static Task<Image> LoadAsync(InputVariant variant, byte[] data, ImageFormat format, ImageDecodeOptions? options, CancellationToken cancellationToken = default)
        => RunAsync(
            variant,
            data,
            format,
            bytes => Image.Load(bytes, options),
            path => Image.Load(path, options),
            stream => Image.Load(stream, options),
            (path, token) => Image.LoadAsync(path, options, token),
            (stream, token) => Image.LoadAsync(stream, options, token),
            cancellationToken);

    /// <summary>Loads <paramref name="data"/> into <typeparamref name="TPixel"/> through a variant.</summary>
    public static Task<Image<TPixel>> LoadAsync<TPixel>(InputVariant variant, byte[] data, ImageFormat format, ImageDecodeOptions? options, CancellationToken cancellationToken = default)
        where TPixel : unmanaged
        => RunAsync(
            variant,
            data,
            format,
            bytes => Image.Load<TPixel>(bytes, options),
            path => Image.Load<TPixel>(path, options),
            stream => Image.Load<TPixel>(stream, options),
            (path, token) => Image.LoadAsync<TPixel>(path, options, token),
            (stream, token) => Image.LoadAsync<TPixel>(stream, options, token),
            cancellationToken);

    /// <summary>Runs an operation through a variant and verifies the stream and file ownership afterward.</summary>
    public static async Task<T> RunAsync<T>(
        InputVariant variant,
        byte[] data,
        ImageFormat format,
        Func<byte[], T> fromSpan,
        Func<string, T> fromPath,
        Func<Stream, T> fromStream,
        Func<string, CancellationToken, Task<T>> fromPathAsync,
        Func<Stream, CancellationToken, Task<T>> fromStreamAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        switch (variant)
        {
            case InputVariant.Span:
                return fromSpan(data);

            case InputVariant.Path:
            case InputVariant.MisleadingExtensionPath:
            case InputVariant.AsyncPath:
            {
                var extension = variant == InputVariant.MisleadingExtensionPath ? GetMisleadingExtension(format) : GetExtension(format);
                var path = WriteTemporaryFile(data, extension);
                try
                {
                    T result;
                    try
                    {
                        result = variant == InputVariant.AsyncPath ? await fromPathAsync(path, cancellationToken).ConfigureAwait(false) : fromPath(path);
                    }
                    catch
                    {
                        EnsureFileClosed(path);
                        throw;
                    }

                    EnsureFileClosed(path);
                    return result;
                }
                finally
                {
                    File.Delete(path);
                }
            }

            default:
            {
                using var stream = CreateStream(variant, data);
                T result;
                try
                {
                    result = variant is InputVariant.AsyncStream or InputVariant.AsyncShortReadStream
                        ? await fromStreamAsync(stream, cancellationToken).ConfigureAwait(false)
                        : fromStream(stream);
                }
                catch
                {
                    EnsureStreamContract(variant, stream, data.Length);
                    throw;
                }

                EnsureStreamContract(variant, stream, data.Length);
                return result;
            }
        }
    }

    private static async Task<T> ConsumeAsync<TPixel, T>(ImageReader<TPixel> reader, bool asynchronous, Func<ImageReader<TPixel>, bool, Task<T>> consume)
        where TPixel : unmanaged
    {
        try
        {
            return await consume(reader, asynchronous).ConfigureAwait(false);
        }
        finally
        {
            if (asynchronous)
            {
                await reader.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                reader.Dispose();
            }
        }
    }

    private static void EnsureStreamContract(InputVariant variant, TestInputStream stream, int length)
    {
        if (stream.IsDisposed)
            throw new GoldenAssertionException($"{variant}: the caller stream was disposed; eager APIs must leave caller streams open.");

        if (stream.BytesRead > length)
            throw new GoldenAssertionException($"{variant}: {stream.BytesRead} bytes were read from a {length}-byte input.");
    }

    /// <summary>Creates the stream of a stream variant.</summary>
    public static TestInputStream CreateStream(InputVariant variant, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return variant switch
        {
            InputVariant.SeekableStream or InputVariant.AsyncStream => new TestInputStream(data) { ForbidSynchronousReads = variant == InputVariant.AsyncStream, ForbidAsynchronousReads = variant == InputVariant.SeekableStream },
            InputVariant.NonSeekableStream => new TestInputStream(data) { Seekable = false, ForbidAsynchronousReads = true },
            InputVariant.ShortReadStream => new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1, ForbidAsynchronousReads = true },
            InputVariant.AsyncShortReadStream => new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 3, ForbidSynchronousReads = true },
            InputVariant.OffsetStream => new TestInputStream([.. OffsetPrefix, .. data], OffsetPrefix.Length) { ForbidAsynchronousReads = true },
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Not a stream variant."),
        };
    }

    private static FullPath WriteTemporaryFile(byte[] data, string extension)
    {
        var directory = FullPath.GetTempPath() / "Meziantou.Framework.Imaging.Tests";
        Directory.CreateDirectory(directory);
        var path = directory / (Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static void EnsureFileClosed(string path)
    {
        try
        {
            // Exclusive access fails while another handle of this process is open
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new GoldenAssertionException($"The file '{path}' is still open after the call returned; path overloads must close their file stream.", ex);
        }
    }
}
