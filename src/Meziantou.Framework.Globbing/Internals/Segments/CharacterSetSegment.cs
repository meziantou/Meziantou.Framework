using System.Buffers;
using Meziantou.Framework;

namespace Meziantou.Framework.Globbing.Internals;

internal sealed class CharacterSetSegment : Segment
{
    private readonly SearchValues<char> _searchValues;
    private readonly bool _ignoreCase;

    public CharacterSetSegment(string set, bool ignoreCase)
    {
        Set = set;
        IgnoreCase = ignoreCase;
        _ignoreCase = ignoreCase;
        _searchValues = SearchValues.Create(ignoreCase ? set.ToUpperInvariant() : set);
    }

    public bool IgnoreCase { get; }

    public string Set { get; }

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.CurrentText.IsEmpty || pathReader.IsEndOfCurrentSegment)
            return false;

        Span<char> currentCharacter = stackalloc char[1];
        currentCharacter[0] = _ignoreCase ? char.ToUpperInvariant(pathReader.CurrentText[0]) : pathReader.CurrentText[0];
        var result = currentCharacter.ContainsAny(_searchValues);
        if (result)
        {
            pathReader.ConsumeInSegment(1);
        }

        return result;
    }

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        sb.Append('[');
        sb.Append(Set);
        sb.Append(']');
        return sb.ToString();
    }
}
