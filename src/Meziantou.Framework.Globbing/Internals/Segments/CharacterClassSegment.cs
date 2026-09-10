namespace Meziantou.Framework.Globbing.Internals;

internal sealed class CharacterClassSegment : Segment
{
    private readonly NamedCharacterClass _characterClass;
    private readonly bool _ignoreCase;

    public CharacterClassSegment(NamedCharacterClass characterClass, bool ignoreCase)
    {
        _characterClass = characterClass;
        _ignoreCase = ignoreCase;
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.IsEndOfCurrentSegment)
            return false;

        var result = IsMatch(pathReader.CurrentText[0]);
        if (result)
        {
            pathReader.ConsumeInSegment(1);
        }

        return result;
    }

    private bool IsMatch(char c)
    {
        // '[[:upper:]]' must accept 'a' when the case is ignored, as 'A' is accepted.
        if (_ignoreCase && _characterClass is NamedCharacterClass.Lower or NamedCharacterClass.Upper)
            return char.IsLower(c) || char.IsUpper(c);

        return _characterClass switch
        {
            NamedCharacterClass.Alnum => char.IsLetterOrDigit(c),
            NamedCharacterClass.Alpha => char.IsLetter(c),
            NamedCharacterClass.Blank => c is ' ' or '\t',
            NamedCharacterClass.Cntrl => char.IsControl(c),
            // POSIX defines the digit class as the characters '0' to '9' in every locale.
            NamedCharacterClass.Digit => char.IsAsciiDigit(c),
            NamedCharacterClass.Graph => IsGraph(c),
            NamedCharacterClass.Lower => char.IsLower(c),
            NamedCharacterClass.Print => IsGraph(c) || c is ' ',
            NamedCharacterClass.Punct => char.IsPunctuation(c) || char.IsSymbol(c),
            NamedCharacterClass.Space => char.IsWhiteSpace(c),
            NamedCharacterClass.Upper => char.IsUpper(c),
            NamedCharacterClass.XDigit => char.IsAsciiHexDigit(c),
            _ => false,
        };

        static bool IsGraph(char c) => !char.IsWhiteSpace(c) && !char.IsControl(c);
    }

    public override string ToString()
    {
        var name = _characterClass switch
        {
            NamedCharacterClass.Alnum => "alnum",
            NamedCharacterClass.Alpha => "alpha",
            NamedCharacterClass.Blank => "blank",
            NamedCharacterClass.Cntrl => "cntrl",
            NamedCharacterClass.Digit => "digit",
            NamedCharacterClass.Graph => "graph",
            NamedCharacterClass.Lower => "lower",
            NamedCharacterClass.Print => "print",
            NamedCharacterClass.Punct => "punct",
            NamedCharacterClass.Space => "space",
            NamedCharacterClass.Upper => "upper",
            NamedCharacterClass.XDigit => "xdigit",
            _ => "",
        };

        return "[[:" + name + ":]]";
    }
}
