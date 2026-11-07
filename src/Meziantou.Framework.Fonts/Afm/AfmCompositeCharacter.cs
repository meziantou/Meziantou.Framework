namespace Meziantou.Framework.Fonts;

/// <summary>A composite character of an AFM file (the <c>CC</c> key): a character made of other characters of the font, such as an accented letter (Adobe Technical Note #5004, section 10).</summary>
public sealed class AfmCompositeCharacter
{
    internal AfmCompositeCharacter(string name, IReadOnlyList<AfmCompositePart> parts)
    {
        Name = name;
        Parts = parts;
    }

    /// <summary>Gets the name of the composite character.</summary>
    public string Name { get; }

    /// <summary>Gets the characters the composite is made of (<c>PCC</c>), in the order of the file.</summary>
    public IReadOnlyList<AfmCompositePart> Parts { get; }
}
