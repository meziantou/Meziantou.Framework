#if NET461 || NETSTANDARD2_0
#elif NETCOREAPP3_1
using System;
using System.Buffers;
using System.Text;

namespace Meziantou.Framework.Text
{
    public static class Utf8Extensions
    {
        public static SpanUtf8BytesRuneEnumerator EnumerateRunesFromUtf8(this ReadOnlySpan<byte> utf8Bytes)
        {
            return new SpanUtf8BytesRuneEnumerator(utf8Bytes);
        }

        public ref struct SpanUtf8BytesRuneEnumerator
        {
            private ReadOnlySpan<byte> _remaining;
            private Rune _current;

            internal SpanUtf8BytesRuneEnumerator(ReadOnlySpan<byte> utf8Bytes)
            {
                _remaining = utf8Bytes;
                _current = default;
            }

            public SpanUtf8BytesRuneEnumerator GetEnumerator() => this;

            public Rune Current => _current;

            public bool MoveNext()
            {
                var operationStatus = Rune.DecodeFromUtf8(_remaining, out _current, out var bytesConsumed);
                _remaining = _remaining[bytesConsumed..];
                return operationStatus == OperationStatus.Done;
            }
        }
    }
}
#else
#error Platform not supported
#endif
