namespace Meziantou.Framework.Fonts;

/// <summary>A named instance of a variable font: a name and a value for each axis, as 16.16 fixed-point numbers.</summary>
internal sealed class FontInstanceRecord
{
    public FontInstanceRecord(int nameId, int postScriptNameId, int[] coordinates)
    {
        NameId = nameId;
        PostScriptNameId = postScriptNameId;
        Coordinates = coordinates;
    }

    /// <summary>Gets the identifier of the name of the instance in the <c>name</c> table.</summary>
    public int NameId { get; }

    /// <summary>Gets the identifier of the PostScript name of the instance, or 0xFFFF when it has none.</summary>
    public int PostScriptNameId { get; }

    public int[] Coordinates { get; }
}
