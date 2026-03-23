namespace Meziantou.Framework.Json.Internals;

internal struct PathComponent
{
    public bool IsIndex;
    public long Index;
    public string? Name;

    public static PathComponent FromIndex(long index) => new() { IsIndex = true, Index = index };

    public static PathComponent FromName(string name) => new() { IsIndex = false, Name = name };
}
