using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework
{
    public static class StreamExtensions
    {
        public static int ReadUntilCountOrEnd(this Stream stream, byte[] buffer, int offset, int count)
        {
            var totalRead = 0;
            while (count > 0)
            {
                var read = stream.Read(buffer, offset + totalRead, count);
                if (read == 0)
                    return totalRead;

                totalRead += read;
                count -= read;
            }

            return totalRead;
        }

        public static Task<int> ReadUntilCountOrEndAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            return ReadUntilCountOrEndAsync(stream, buffer.AsMemory(offset, count), cancellationToken);
        }

        public static async Task<int> ReadUntilCountOrEndAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var totalRead = 0;
            while (!buffer.IsEmpty)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return totalRead;

                totalRead += read;
                buffer = buffer[read..];
            }

            return totalRead;
        }
    }
}
