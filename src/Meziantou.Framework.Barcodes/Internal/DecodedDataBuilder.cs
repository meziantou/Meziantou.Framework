namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Collects the bytes, the FNC1 characters and the ECI designators read from a symbol, and decodes the bytes with the
/// character set of the ECI designator that precedes them.
/// </summary>
internal sealed class DecodedDataBuilder
{
    private const byte GroupSeparator = 0x1D;

    private readonly List<byte> _bytes = [];
    private readonly List<byte> _pending = [];
    private readonly StringBuilder _text = new();
    private int? _eci;

    /// <summary>Gets the assignment number of the first ECI designator, or <see langword="null"/>.</summary>
    public int? FirstEci { get; private set; }

    public int ByteCount => _bytes.Count;

    public void AppendByte(int value)
    {
        _bytes.Add((byte)value);
        _pending.Add((byte)value);
    }

    /// <summary>Appends a FNC1 character, which separates the elements of GS1 data: it reads as a GS character.</summary>
    public void AppendFnc1()
    {
        Flush();
        _bytes.Add(GroupSeparator);
        _text.Append('\u001D');
    }

    public void AppendText(string value)
    {
        Flush();
        foreach (var c in value)
        {
            _bytes.Add((byte)c);
        }

        _text.Append(value);
    }

    public void SetEci(int assignmentNumber)
    {
        // A repeated ECI designator does not split the bytes of a character
        if (_eci != assignmentNumber)
        {
            Flush();
            _eci = assignmentNumber;
        }

        FirstEci ??= assignmentNumber;
    }

    public byte[] GetBytes() => [.. _bytes];

    /// <summary>Gets the text, or its human-readable interpretation for GS1 data when its element strings can be read.</summary>
    public string GetText(bool isGs1)
    {
        Flush();
        var text = _text.ToString();
        return isGs1 ? Gs1ElementStrings.FormatHumanReadable(text) ?? text : text;
    }

    private void Flush()
    {
        if (_pending.Count is 0)
            return;

        _text.Append(EciCharacterSets.GetString(_pending.ToArray(), _eci));
        _pending.Clear();
    }
}
