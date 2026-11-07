using System.Buffers;

namespace Meziantou.Framework.MediaTags.Internals;

internal static class StreamHelpers
{
    /// <summary>The largest amount of data read into memory for a single tag record.</summary>
    /// <remarks>
    /// Record sizes are declared by the file, so they must be checked before they are used as an allocation
    /// size. This is the single limit for every format; keep it in one place so hardening one parser hardens
    /// all of them.
    /// </remarks>
    public const int MaxRecordDataSize = 10 * 1024 * 1024;

    private const int CopyBufferSize = 8192;

    /// <summary>
    /// Reads <paramref name="declaredSize"/> bytes, after validating the declared size against the bytes that
    /// are actually available and against <paramref name="limit"/>.
    /// </summary>
    public static bool TryReadExact(Stream stream, long declaredSize, long limit, [NotNullWhen(true)] out byte[]? data)
    {
        data = null;

        if (declaredSize < 0 || declaredSize > limit)
            return false;

        if (stream.CanSeek && declaredSize > stream.Length - stream.Position)
            return false;

        var buffer = new byte[declaredSize];
        if (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) < buffer.Length)
            return false;

        data = buffer;
        return true;
    }

    /// <summary>
    /// Copies exactly <paramref name="count"/> bytes from one stream to another.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the source ends before <paramref name="count"/> bytes were copied. A writer
    /// must fail in that case: the output replaces the caller's file, so a short copy silently destroys audio.
    /// </returns>
    public static bool CopyExactly(Stream source, Stream destination, long count)
    {
        if (count < 0)
            return false;

        if (count == 0)
            return true;

        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(count, CopyBufferSize));
        try
        {
            while (count > 0)
            {
                var toRead = (int)Math.Min(count, buffer.Length);
                var read = source.Read(buffer, 0, toRead);
                if (read == 0)
                    return false;

                destination.Write(buffer, 0, read);
                count -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return true;
    }

    /// <summary>
    /// Copies <paramref name="count"/> bytes starting at <paramref name="position"/>, leaving the source
    /// positioned after the copied range.
    /// </summary>
    public static bool CopyExactlyFrom(Stream source, Stream destination, long position, long count)
    {
        source.Position = position;
        return CopyExactly(source, destination, count);
    }
}
