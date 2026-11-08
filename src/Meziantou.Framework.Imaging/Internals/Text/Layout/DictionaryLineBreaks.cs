using System.Buffers;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The line break opportunities between the words of Thai, Lao, Khmer and Burmese, found with the word lists and the
/// dictionary break engines of ICU (<see cref="IcuDictionaryBreakEngine"/>), as the line break iterator of ICU finds them.
/// </summary>
/// <remarks>
/// <para>
/// The Unicode line breaking algorithm resolves the characters of the class Complex_Context (SA) to letters and marks: it
/// never breaks a run of them. As ICU does (<c>RuleBasedBreakIterator::DictionaryCache::populateDictionary</c>), the text
/// between two opportunities of the algorithm is given to the engines: a run is a maximal sequence of the characters an
/// engine handles in that text, and the engine gives the starts of its words, other than the start of the run. A run
/// does not cross an opportunity of the algorithm: a combining mark that follows another character, such as a hyphen,
/// belongs to that character (rule LB9), and the run starts after it.
/// </para>
/// <para>An instance reuses its buffer: it is not thread-safe.</para>
/// </remarks>
internal sealed class DictionaryLineBreaks : IDisposable
{
    private int[]? _buffer;

    /// <summary>Adds the starts of the words of the runs of a part of a text between two opportunities of the Unicode line breaking algorithm, in increasing order.</summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The start of the part.</param>
    /// <param name="end">The end of the part.</param>
    /// <param name="breaks">The list the positions are added to, as indexes in <paramref name="text"/>.</param>
    public void AddBreaks(ReadOnlySpan<char> text, int start, int end, List<int> breaks)
    {
        var index = start;
        while (index < end)
        {
            var engine = GetEngine(text, index, out var length);
            if (engine is null)
            {
                index += length;
                continue;
            }

            var runStart = index;
            index += length;
            while (index < end && GetEngine(text, index, out length) == engine)
            {
                index += length;
            }

            var runLength = index - runStart;
            if (runLength < 2)
                continue;

            if (_buffer is null || _buffer.Length < runLength)
            {
                if (_buffer is not null)
                {
                    ArrayPool<int>.Shared.Return(_buffer);
                }

                _buffer = ArrayPool<int>.Shared.Rent(runLength);
            }

            var count = engine.FindBreaks(text.Slice(runStart, runLength), _buffer);
            foreach (var position in _buffer.AsSpan(0, count))
            {
                breaks.Add(runStart + position);
            }
        }
    }

    public void Dispose()
    {
        if (_buffer is not null)
        {
            ArrayPool<int>.Shared.Return(_buffer);
            _buffer = null;
        }
    }

    /// <summary>Gets the engine of the code point at an index, other than the one of Chinese and Japanese, whose lines break between any two of their characters.</summary>
    private static IcuDictionaryBreakEngine? GetEngine(ReadOnlySpan<char> text, int index, out int length)
    {
        // No engine handles an unpaired surrogate, which is read as U+FFFD, nor a character before U+0E01 (Thai).
        Rune.DecodeFromUtf16(text[index..], out var rune, out length);
        if (rune.Value < 0x0E01)
            return null;

        return IcuDictionaryBreakEngine.GetEngine(rune.Value) is { DictionaryKind: not IcuWordDictionaryKind.ChineseJapanese } engine ? engine : null;
    }
}
