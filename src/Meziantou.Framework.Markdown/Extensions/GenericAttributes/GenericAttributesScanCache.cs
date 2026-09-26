using System.Buffers;

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// Remembers which '{' of an inline text cannot start generic attributes, so that they fail without scanning the rest
/// of the text again. Without it, inputs such as <c>{#a</c> or <c>={a</c> repeated n times take O(n²) time.
/// </summary>
internal sealed class GenericAttributesScanCache
{
    private string? _text;
    private int _textEnd;
    private int _firstScanStart;
    private long _failedScanLength;
    private byte[]? _scanOutcomes;
    private int _scanOutcomesStart;

    /// <summary>
    /// Forgets everything remembered unless <paramref name="text"/> is the text the previous scans ran on.
    /// </summary>
    public void SetText(StringSlice text)
    {
        // The outcome of a scan only depends on the characters between its start and the end of the text
        if (!ReferenceEquals(_text, text.Text) || _textEnd != text.End)
        {
            Clear();
            _text = text.Text;
            _textEnd = text.End;
            _firstScanStart = text.Start;
        }
    }

    /// <summary>
    /// Forgets everything remembered.
    /// </summary>
    public void Clear()
    {
        _text = null;
        _textEnd = 0;
        _firstScanStart = 0;
        _failedScanLength = 0;
        _scanOutcomesStart = 0;
        if (_scanOutcomes is not null)
        {
            ArrayPool<byte>.Shared.Return(_scanOutcomes);
            _scanOutcomes = null;
        }
    }

    /// <summary>
    /// Determines whether the generic attributes starting with the '{' at <paramref name="openingBracePosition"/> are known to be invalid.
    /// </summary>
    public bool IsKnownInvalid(int openingBracePosition)
    {
        var index = openingBracePosition + 1 - _scanOutcomesStart;
        return _scanOutcomes is not null && index >= 0 && index <= _textEnd + 1 - _scanOutcomesStart && !GenericAttributesParser.IsValidScanOutcome(_scanOutcomes[index]);
    }

    /// <summary>
    /// Remembers that the generic attributes starting with the '{' at <paramref name="openingBracePosition"/> are invalid,
    /// and that finding it out required to scan the text up to <paramref name="scanEnd"/>.
    /// </summary>
    public void AddFailedScan(int openingBracePosition, int scanEnd)
    {
        if (_scanOutcomes is not null || _text is null)
            return;

        // Scanning each '{' is linear in the length of the scanned text, so the scans are repeated until they cost as much
        // as the rest of the text. Past that point, the outcome of all the scans that can follow is computed once, in linear time.
        _failedScanLength += Math.Max(1, scanEnd - openingBracePosition);
        if (_failedScanLength > _textEnd + 1 - _firstScanStart)
        {
            // The next scans start after this '{'
            var start = openingBracePosition + 1;
            var length = _textEnd + 2 - start;
            if (length > 0)
            {
                _scanOutcomes = ArrayPool<byte>.Shared.Rent(length);
                _scanOutcomesStart = start;
                GenericAttributesParser.ComputeScanOutcomes(_text, start, _textEnd, _scanOutcomes.AsSpan(0, length));
            }
        }
    }
}
