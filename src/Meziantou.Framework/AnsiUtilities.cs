using System.Buffers;

namespace Meziantou.Framework;

public static class AnsiUtilities
{
    // Sequences shorter than this are processed using a stack-allocated buffer to avoid renting an array.
    private const int StackAllocThreshold = 512;

    public static string RemoveAnsiSequences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var firstEscapeIndex = value.AsSpan().IndexOf('\x1b');
        if (firstEscapeIndex < 0)
            return value;

        return RemoveAnsiSequencesCore(value.AsSpan(), firstEscapeIndex, originalIfUnchanged: value);
    }

    public static string RemoveAnsiSequences(ReadOnlySpan<char> value)
    {
        var firstEscapeIndex = value.IndexOf('\x1b');
        if (firstEscapeIndex < 0)
            return value.ToString();

        return RemoveAnsiSequencesCore(value, firstEscapeIndex, originalIfUnchanged: null);
    }

    private static string RemoveAnsiSequencesCore(ReadOnlySpan<char> value, int firstEscapeIndex, string? originalIfUnchanged)
    {
        char[]? rented = null;
        var buffer = value.Length <= StackAllocThreshold
            ? stackalloc char[StackAllocThreshold]
            : (rented = ArrayPool<char>.Shared.Rent(value.Length));

        try
        {
            var written = 0;
            var copiedFrom = 0;
            var searchFrom = firstEscapeIndex;
            var removedAny = false;

            while (true)
            {
                var relativeIndex = value[searchFrom..].IndexOf('\x1b');
                if (relativeIndex < 0)
                    break;

                var escapeIndex = searchFrom + relativeIndex;

                // A removable sequence is a CSI: ESC '[' ... <terminator in 0x40-0x7E>
                if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] == '[')
                {
                    var terminator = FindCsiTerminator(value, escapeIndex + 2);
                    if (terminator >= 0)
                    {
                        var segment = value[copiedFrom..escapeIndex];
                        segment.CopyTo(buffer[written..]);
                        written += segment.Length;

                        copiedFrom = terminator + 1;
                        searchFrom = terminator + 1;
                        removedAny = true;
                        continue;
                    }

                    // Incomplete sequence with no terminator: the rest of the string is kept as-is.
                    break;
                }

                // Lone ESC (not part of a CSI sequence): kept as-is.
                searchFrom = escapeIndex + 1;
            }

            if (!removedAny)
                return originalIfUnchanged ?? value.ToString();

            var tail = value[copiedFrom..];
            tail.CopyTo(buffer[written..]);
            written += tail.Length;

            return new string(buffer[..written]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static int FindCsiTerminator(ReadOnlySpan<char> value, int start)
    {
        for (var i = start; i < value.Length; i++)
        {
            if (value[i] is >= (char)0x40 and <= (char)0x7E)
                return i;
        }

        return -1;
    }

    public static bool ContainsAnsiSequences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return ContainsAnsiSequences(value.AsSpan());
    }

    public static bool ContainsAnsiSequences(ReadOnlySpan<char> value)
    {
        var i = 0;
        while (i < value.Length)
        {
            var escapeIndex = value[i..].IndexOf('\x1b');
            if (escapeIndex < 0)
                return false;

            escapeIndex += i;
            if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] == '[')
                return true;

            i = escapeIndex + 1;
        }

        return false;
    }
}
