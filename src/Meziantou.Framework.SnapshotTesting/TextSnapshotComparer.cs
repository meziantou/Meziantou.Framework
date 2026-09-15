namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Compares text snapshots while treating <c>\r\n</c>, <c>\r</c> and <c>\n</c> as the same line ending. A verified
/// file keeps whatever line endings the editor, the merge tool or git's <c>core.autocrlf</c> gave it, and that must
/// not make a snapshot fail on another operating system. The comparison works on the UTF-8 bytes directly: CR and LF
/// are ASCII, so they can never be part of a multi-byte sequence.
/// </summary>
internal sealed class TextSnapshotComparer : ISnapshotComparer
{
    public static TextSnapshotComparer Instance { get; } = new();

    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        ReadOnlySpan<byte> expectedData = expected.Data;
        ReadOnlySpan<byte> actualData = actual.Data;
        if (expectedData.SequenceEqual(actualData))
            return true;

        while (true)
        {
            var index = expectedData.IndexOfAny((byte)'\r', (byte)'\n');
            if (index < 0)
                return expectedData.SequenceEqual(actualData);

            if (actualData.Length <= index || !expectedData[..index].SequenceEqual(actualData[..index]) || !IsEndOfLine(actualData[index]))
                return false;

            expectedData = SkipEndOfLine(expectedData[index..]);
            actualData = SkipEndOfLine(actualData[index..]);
        }
    }

    private static bool IsEndOfLine(byte value) => value is (byte)'\r' or (byte)'\n';

    private static ReadOnlySpan<byte> SkipEndOfLine(ReadOnlySpan<byte> data)
    {
        if (data is [(byte)'\r', (byte)'\n', ..])
            return data[2..];

        return data[1..];
    }
}
