namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
internal static class StreamExtensions
{
#if NET6_0
    public static int ReadAtLeast(this Stream stream, Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream = true)
    {
        return ReadAtLeastCore(stream, buffer, minimumBytes, throwOnEndOfStream);
    }

    private static int ReadAtLeastCore(Stream stream, Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream)
    {
        int totalRead = 0;
        while (totalRead < minimumBytes)
        {
            var read = stream.Read(buffer.Slice(totalRead));
            if (read == 0)
            {
                if (throwOnEndOfStream)
                    throw new EndOfStreamException("Unable to read beyond the end of the stream.");

                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }
#elif NETSTANDARD2_0
    public static int ReadAtLeast(this Stream stream, byte[] buffer, int minimumBytes, bool throwOnEndOfStream = true)
    {
        return ReadAtLeastCore(stream, buffer, minimumBytes, throwOnEndOfStream);
    }

    private static int ReadAtLeastCore(Stream stream, byte[] buffer, int minimumBytes, bool throwOnEndOfStream)
    {
        int totalRead = 0;
        while (totalRead < minimumBytes)
        {
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
            {
                if (throwOnEndOfStream)
                    throw new EndOfStreamException("Unable to read beyond the end of the stream.");

                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }
#endif
}
