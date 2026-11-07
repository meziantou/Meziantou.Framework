namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>The definition of a kind of object: the rows of its TSV file.</summary>
public sealed class ArlingtonObjectDefinition
{
    private ArlingtonObjectDefinition(string name, IReadOnlyList<ArlingtonKey> keys)
    {
        Name = name;
        Keys = keys;
        IsArray = keys.Count > 0 && keys.All(k => k.Name == "*" || char.IsAsciiDigit(k.Name[0]));
    }

    /// <summary>Gets the name of the definition (the name of the TSV file).</summary>
    public string Name { get; }

    /// <summary>Gets the keys: the names of the entries of a dictionary, or the indexes of the elements of an array.</summary>
    public IReadOnlyList<ArlingtonKey> Keys { get; }

    /// <summary>Gets a value indicating whether the definition describes an array: its keys are indexes.</summary>
    public bool IsArray { get; }

    internal static ArlingtonObjectDefinition Parse(string name, string[] lines)
    {
        var keys = new List<ArlingtonKey>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
                continue;

            var columns = line.Split('\t');
            if (columns.Length < 11)
                continue;

            keys.Add(ArlingtonKey.Parse(columns));
        }

        return new ArlingtonObjectDefinition(name, keys);
    }
}
