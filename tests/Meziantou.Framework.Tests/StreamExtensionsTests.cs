using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class StreamExtensionsTests
    {
        [Fact]
        public void ReadUntilEndTests()
        {
            using var stream = new CustomStream();
            var buffer = new byte[5];
            stream.ReadUntilCountOrEnd(buffer, 0, 5);

            Assert.Equal(new byte[] { 0, 1, 2, 0, 0 }, buffer);
        }

        [Fact]
        public async Task ReadUntilEndAsyncTests()
        {
            using var stream = new CustomStream();
            var buffer = new byte[5];
            await stream.ReadUntilCountOrEndAsync(buffer, 0, 5);

            Assert.Equal(new byte[] { 0, 1, 2, 0, 0 }, buffer);
        }

        private sealed class CustomStream : Stream
        {
            public override bool CanRead => throw new NotSupportedException();
            public override bool CanSeek => throw new NotSupportedException();
            public override bool CanWrite => throw new NotSupportedException();
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (offset == 3)
                    return 0;

                buffer[offset] = (byte)offset;
                return 1;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (offset == 3)
                    return 0;

                buffer[offset] = (byte)offset;
                await Task.Yield();
                return 1;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
