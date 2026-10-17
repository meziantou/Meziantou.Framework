using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// Reads reference buffers described by the manifest. Before returning pixels it validates the descriptor (layout, row
/// byte count, exact length, alpha mode, row order), the stored file size, the SHA-256 hash, and, for compressed storage,
/// decompresses with a bounded non-image decompressor (gzip) that stops one byte past the declared length.
/// </summary>
public static class RawBufferReader
{
    /// <summary>The maximum decoded size of one reference buffer (keeps the corpus small and decompression bounded).</summary>
    public const int MaxBufferBytes = 16 * 1024 * 1024;

    /// <summary>Gzip may slightly expand incompressible data: stored files may exceed the decoded length by this margin.</summary>
    public const int MaxCompressionOverheadBytes = 1024;

    /// <summary>The accepted <see cref="RawBufferDescriptor.Compression"/> values.</summary>
    public static IReadOnlySet<string> AllowedCompressions { get; } = new HashSet<string>(StringComparer.Ordinal) { "none", "gzip" };

    /// <summary>Validates a descriptor without reading the file.</summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <param name="canvasWidth">The expected width (the fixture canvas), or <see langword="null"/>.</param>
    /// <param name="canvasHeight">The expected height, or <see langword="null"/>.</param>
    /// <returns>The errors; empty when valid.</returns>
    public static IReadOnlyList<string> ValidateDescriptor(RawBufferDescriptor descriptor, int? canvasWidth = null, int? canvasHeight = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var errors = new List<string>();
        var name = $"buffer '{descriptor.Path}'";
        if (!RawPixelLayout.TryParse(descriptor.Layout, out var layout))
        {
            errors.Add($"{name}: unknown layout '{descriptor.Layout}' (known: {string.Join(", ", RawPixelLayout.All)}).");
            return errors;
        }

        if (descriptor.Width <= 0 || descriptor.Height <= 0)
        {
            errors.Add($"{name}: dimensions must be positive.");
            return errors;
        }

        if ((canvasWidth is not null && descriptor.Width != canvasWidth) || (canvasHeight is not null && descriptor.Height != canvasHeight))
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: {descriptor.Width}x{descriptor.Height} does not match the {canvasWidth}x{canvasHeight} canvas (frames are full-canvas displayed frames)."));
        }

        long rowBytes = (long)descriptor.Width * layout.BytesPerPixel;
        if (descriptor.RowBytes != rowBytes)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: rowBytes {descriptor.RowBytes} does not match width x bytes per pixel = {rowBytes} (buffers are tightly packed)."));
        }

        var byteLength = rowBytes * descriptor.Height;
        if (descriptor.ByteLength != byteLength)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: byteLength {descriptor.ByteLength} does not match rowBytes x height = {byteLength}."));
        }

        if (byteLength > MaxBufferBytes)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: {byteLength} bytes exceeds the {MaxBufferBytes}-byte limit for a reference buffer; keep fixtures tiny."));
        }

        var expectedAlpha = layout.HasAlpha ? "straight" : "none";
        if (!string.Equals(descriptor.Alpha, expectedAlpha, StringComparison.Ordinal))
        {
            errors.Add($"{name}: alpha '{descriptor.Alpha}' is not valid for layout {layout}; expected '{expectedAlpha}' (references store straight alpha).");
        }

        if (!string.Equals(descriptor.RowOrder, "top-down", StringComparison.Ordinal))
        {
            errors.Add($"{name}: rowOrder '{descriptor.RowOrder}' is not supported (only 'top-down').");
        }

        if (!AllowedCompressions.Contains(descriptor.Compression))
        {
            errors.Add($"{name}: compression '{descriptor.Compression}' is not supported (allowed: {string.Join(", ", AllowedCompressions)}).");
        }

        return errors;
    }

    /// <summary>Reads and validates a reference buffer.</summary>
    /// <param name="rootDirectory">The corpus root.</param>
    /// <param name="descriptor">The descriptor.</param>
    /// <returns>The buffer.</returns>
    /// <exception cref="InvalidDataException">The descriptor, file size, hash or decompressed length is invalid.</exception>
    public static RawPixelBuffer Read(FullPath rootDirectory, RawBufferDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var errors = ValidateDescriptor(descriptor);
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(" ", errors));

        var path = FixturePaths.TryResolve(rootDirectory, descriptor.Path, out var pathError)
            ?? throw new InvalidDataException($"buffer '{descriptor.Path}': {pathError}");
        if (!File.Exists(path))
            throw new InvalidDataException($"buffer '{descriptor.Path}': file does not exist.");

        var layout = RawPixelLayout.Parse(descriptor.Layout);
        var fileLength = new FileInfo(path).Length;
        var compressed = descriptor.Compression == "gzip";
        if (!compressed && fileLength != descriptor.ByteLength)
            throw new InvalidDataException($"buffer '{descriptor.Path}': {RawPixelBuffer.DescribeLengthMismatch(descriptor.Width, descriptor.Height, layout, fileLength)}");

        if (compressed && fileLength > (long)descriptor.ByteLength + MaxCompressionOverheadBytes)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"buffer '{descriptor.Path}': the compressed file ({fileLength} bytes) is larger than the decoded length plus {MaxCompressionOverheadBytes} bytes."));

        var stored = File.ReadAllBytes(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stored));
        if (!string.Equals(hash, descriptor.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException($"buffer '{descriptor.Path}': sha256 {hash} does not match the manifest ({descriptor.Sha256}). Regenerating references is an explicit, reviewed operation.");

        var data = compressed ? Decompress(descriptor, stored) : stored;
        return RawPixelBuffer.Create(descriptor.Width, descriptor.Height, layout, data);
    }

    private static byte[] Decompress(RawBufferDescriptor descriptor, byte[] stored)
    {
        // Bounded: never read more than one byte past the declared length, whatever the compressed stream claims
        var buffer = new byte[descriptor.ByteLength + 1];
        var total = 0;
        try
        {
            using var gzip = new GZipStream(new MemoryStream(stored), CompressionMode.Decompress);
            int read;
            while (total < buffer.Length && (read = gzip.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"buffer '{descriptor.Path}': invalid gzip data.", ex);
        }

        if (total > descriptor.ByteLength)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"buffer '{descriptor.Path}': decompresses to more than the declared {descriptor.ByteLength} bytes (bounded decompression stopped)."));

        if (total < descriptor.ByteLength)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"buffer '{descriptor.Path}': decompresses to {total} bytes but {descriptor.ByteLength} are declared (truncated)."));

        return buffer.AsSpan(0, total).ToArray();
    }
}
