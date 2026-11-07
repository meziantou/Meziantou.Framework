namespace Meziantou.Framework.Fonts;

/// <summary>A named instance of a variable font: a name and a value for each axis, as 16.16 fixed-point numbers.</summary>
public sealed class FontInstanceRecord
{
    private readonly int[] _coordinates;

    internal FontInstanceRecord(int nameId, int postScriptNameId, int[] coordinates)
    {
        NameId = nameId;
        PostScriptNameId = postScriptNameId;
        _coordinates = coordinates;
    }

    /// <summary>Gets the identifier of the name of the instance in the <c>name</c> table.</summary>
    public int NameId { get; }

    /// <summary>Gets the identifier of the PostScript name of the instance, or 0xFFFF when it has none.</summary>
    public int PostScriptNameId { get; }

    /// <summary>Gets the value of each axis, in the order of the axes of the font, as 16.16 fixed-point numbers.</summary>
    public ReadOnlySpan<int> Coordinates => _coordinates;
}
