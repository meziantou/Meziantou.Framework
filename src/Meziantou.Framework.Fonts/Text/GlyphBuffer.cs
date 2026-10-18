namespace Meziantou.Framework.Fonts;

/// <summary>
/// The glyphs of a run of text while it is shaped: characters come in, substitutions replace, insert and remove glyphs,
/// positioning fills the positions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Clusters.</b> Every glyph carries the index of the first character of its cluster. Glyphs that shaping ties
/// together (a ligature, a reordered syllable, a character and its marks) share one cluster, the smallest index of
/// their characters, and the clusters never decrease along a run. A cluster is what a caret, a selection or a line
/// break can separate.
/// </para>
/// <para>
/// <b>Bounds.</b> A font is untrusted input: its lookups can multiply glyphs without end. The buffer refuses to grow
/// beyond <see cref="FontResourceLimits.MaxTextGlyphs"/>, and counts the operations of the lookups so that a font
/// cannot make shaping run for a time out of proportion with the text: a pass of a lookup over the glyphs is what
/// shaping is, the lookups a rule applies and the number of subtables of the font are what is bounded.
/// </para>
/// </remarks>
internal sealed class GlyphBuffer
{
    /// <summary>The lookups the rules of the lookups of a run can apply, per character: far more than any font needs.</summary>
    private const int OperationsPerCharacter = 1024;

    private const int MinimumOperations = 16384;

    /// <summary>The subtables the lookups of one run can have, together: far more than any font has.</summary>
    private const int MaxSubtablesPerRun = 1 << 16;

    private readonly long _maxLength;
    private GlyphInfo[] _info = new GlyphInfo[32];

    // The positions, once they are created: the first _positionCount entries of a storage that is kept from a run to
    // the next one.
    private GlyphPosition[] _positionStorage = [];
    private int _positionCount;
    private long _operations;
    private long _subtables;
    private byte _nextLigatureId;

    public GlyphBuffer(FontResourceLimits limits)
    {
        _maxLength = limits.MaxTextGlyphs;
    }

    /// <summary>Gets the number of glyphs.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the glyphs.</summary>
    public Span<GlyphInfo> Info => _info.AsSpan(0, Length);

    /// <summary>Gets the positions of the glyphs, once <see cref="CreatePositions"/> was called.</summary>
    public Span<GlyphPosition> Positions => _positionStorage.AsSpan(0, _positionCount)[..Length];

    /// <summary>Removes every glyph.</summary>
    public void Clear()
    {
        Length = 0;
        _operations = 0;
        _nextLigatureId = 0;
        _positionCount = 0;
    }

    /// <summary>Adds a character at the end.</summary>
    public void Add(int codePoint, int cluster)
    {
        EnsureCapacity(Length + 1L);
        _info[Length++] = new GlyphInfo { Glyph = codePoint, CodePoint = codePoint, Cluster = cluster };
    }

    /// <summary>Sets the budget of operations from the number of characters of the run.</summary>
    public void StartShaping()
    {
        _operations = Math.Max((long)Length * OperationsPerCharacter, MinimumOperations);
        _subtables = 0;
    }

    /// <summary>
    /// Counts a pass of a lookup over the glyphs: each of its subtables is tried at each glyph. That work is what
    /// shaping is, and is in proportion with the text as long as the lookups of the font are in reasonable number;
    /// what a lookup costs beyond it (the lookups its rules apply) is taken from the budget of the run.
    /// </summary>
    /// <exception cref="InvalidFontException">The lookups have more subtables than a font can need.</exception>
    public void StartPass(int subtableCount)
    {
        _subtables += subtableCount;
        if (_subtables > MaxSubtablesPerRun)
            throw FontErrors.Invalid("its layout tables make shaping a text take an unreasonable amount of work.");
    }

    /// <summary>Spends operations of the budget.</summary>
    /// <exception cref="InvalidFontException">The font asks for more work than a text can need.</exception>
    public void Charge(int count)
    {
        _operations -= count;
        if (_operations < 0)
            throw FontErrors.Invalid("its layout tables make shaping a text take an unreasonable amount of work.");
    }

    /// <summary>Allocates the positions, all zero, once the substitutions are done.</summary>
    public void CreatePositions()
    {
        EnsurePositionCapacity(Length);
        _positionStorage.AsSpan(0, Length).Clear();
        _positionCount = Length;
    }

    /// <summary>Gets an identifier for a new ligature: a small number that differs from the ones of its neighbors.</summary>
    public byte AllocateLigatureId()
    {
        _nextLigatureId = (byte)(_nextLigatureId == byte.MaxValue ? 1 : _nextLigatureId + 1);
        return _nextLigatureId;
    }

    /// <summary>
    /// Replaces a range of glyphs by a number of glyphs, which are copies of the first glyph of the range (or of the
    /// glyph before the insertion point when the range is empty) and are then set by the caller.
    /// </summary>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public void Resize(int index, int oldCount, int newCount)
    {
        if (oldCount == newCount)
            return;

        var newLength = (long)Length - oldCount + newCount;
        EnsureCapacity(newLength);
        var template = oldCount > 0 ? _info[index] : index > 0 ? _info[index - 1] : default;
        Array.Copy(_info, index + oldCount, _info, index + newCount, Length - index - oldCount);
        for (var i = oldCount > 0 ? 1 : 0; i < newCount; i++)
        {
            _info[index + i] = template;
        }

        Length = (int)newLength;
    }

    /// <summary>Gives a range of glyphs one cluster: the smallest of theirs, extended to the glyphs that already share a cluster with an end of the range.</summary>
    public void MergeClusters(int start, int end)
    {
        if (end - start < 2)
            return;

        var cluster = _info[start].Cluster;
        for (var i = start + 1; i < end; i++)
        {
            cluster = Math.Min(cluster, _info[i].Cluster);
        }

        // The glyphs of a cluster stay together: the neighbors that share the cluster of an end join the range.
        while (end < Length && _info[end - 1].Cluster == _info[end].Cluster)
        {
            end++;
        }

        while (start > 0 && _info[start - 1].Cluster == _info[start].Cluster)
        {
            start--;
        }

        for (var i = start; i < end; i++)
        {
            _info[i].Cluster = cluster;
        }
    }

    /// <summary>Removes a glyph, and its position when the positions exist; its characters join the cluster of a neighbor, so that no character is lost.</summary>
    public void Delete(int index)
    {
        if (_positionCount == Length)
        {
            _positionStorage.AsSpan(index + 1, _positionCount - index - 1).CopyTo(_positionStorage.AsSpan(index));
            _positionCount--;
        }

        var cluster = _info[index].Cluster;
        if (index + 1 < Length && _info[index + 1].Cluster == cluster)
        {
            // The cluster continues after the glyph.
        }
        else if (index > 0)
        {
            // The glyph ends its cluster: the cluster before takes its characters when it starts later.
            if (_info[index - 1].Cluster > cluster)
            {
                var previous = _info[index - 1].Cluster;
                for (var i = index - 1; i >= 0 && _info[i].Cluster == previous; i--)
                {
                    _info[i].Cluster = cluster;
                }
            }
        }
        else if (index + 1 < Length)
        {
            MergeClusters(index, index + 2);
        }

        Resize(index, 1, 0);
    }

    /// <summary>Inserts copies of a positioned glyph after it.</summary>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public void Repeat(int index, int copies)
    {
        if (copies <= 0)
            return;

        var newLength = (long)Length + copies;
        EnsureCapacity(newLength);
        // The positions of the glyphs, which exist: the copies get the position of the glyph.
        var tail = _positionStorage.AsSpan(0, _positionCount).Slice(index + 1, Length - index - 1);
        EnsurePositionCapacity((int)newLength);
        tail = _positionStorage.AsSpan(index + 1, tail.Length);
        tail.CopyTo(_positionStorage.AsSpan(index + 1 + copies));
        Array.Copy(_info, index + 1, _info, index + 1 + copies, Length - index - 1);
        for (var i = 1; i <= copies; i++)
        {
            _info[index + i] = _info[index];
            _positionStorage[index + i] = _positionStorage[index];
        }

        _positionCount = (int)newLength;
        Length = (int)newLength;
    }

    /// <summary>Reverses the order of the glyphs, and of their positions when they exist.</summary>
    public void Reverse()
    {
        Info.Reverse();
        if (_positionCount == Length)
        {
            Positions.Reverse();
        }
    }

    private void EnsurePositionCapacity(int length)
    {
        if (length > _positionStorage.Length)
        {
            Array.Resize(ref _positionStorage, Math.Max(length, _positionStorage.Length * 2));
        }
    }

    private void EnsureCapacity(long length)
    {
        if (length <= _info.Length)
            return;

        if (length > _maxLength)
            throw new FontResourceLimitException(FontResourceLimitKind.TextGlyphs, _maxLength, length);

        Array.Resize(ref _info, (int)Math.Min(Math.Max(length, (long)_info.Length * 2), Math.Max(_maxLength, 32)));
    }
}
