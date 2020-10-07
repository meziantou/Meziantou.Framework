using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.DependencyScanning.Internals
{
    internal static class StreamUtilities
    {
        public static StreamReader CreateReader(Stream stream, Encoding encoding)
        {
            return new StreamReader(stream, encoding, leaveOpen: true);
        }

        public static async ValueTask<StreamReader> CreateReaderAsync(Stream stream, CancellationToken token)
        {
            var encoding = await GetEncodingAsync(stream, token).ConfigureAwait(false);
            stream.Seek(0, SeekOrigin.Begin);

            return CreateReader(stream, encoding);
        }

        public static StreamWriter CreateWriter(Stream stream, Encoding encoding)
        {
            return new StreamWriter(stream, encoding, leaveOpen: true);
        }

        internal static async ValueTask<Encoding> GetEncodingAsync(Stream stream, CancellationToken cancellationToken)
        {
            // Read the BOM
            var bom = new byte[4];
            var readCount = await ReadUntilCountOrEndAsync(stream, bom, cancellationToken).ConfigureAwait(false);

            // Analyze the BOM
            if (readCount >= 3 && bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76)
#pragma warning disable SYSLIB0001 // Type or member is obsolete
                return Encoding.UTF7;
#pragma warning restore SYSLIB0001

            if (readCount >= 3 && bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf)
                return Encoding.UTF8;

            if (readCount >= 2 && bom[0] == 0xff && bom[1] == 0xfe)
                return Encoding.Unicode; //UTF-16LE

            if (readCount >= 2 && bom[0] == 0xfe && bom[1] == 0xff)
                return Encoding.BigEndianUnicode; //UTF-16BE

            if (readCount >= 4 && bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff)
                return Encoding.UTF32;

            return Encoding.Default;
        }

        private static async ValueTask<int> ReadUntilCountOrEndAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var totalRead = 0;
            var count = buffer.Length;
            while (count > 0)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return totalRead;

                totalRead += read;
                count -= read;
            }

            return totalRead;
        }
    }
}
