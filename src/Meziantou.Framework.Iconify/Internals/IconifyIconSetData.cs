namespace Meziantou.Framework.Internals;

internal sealed class IconifyIconSetData
{
    public IconifyIconSetData(string prefix, IconifyIconSetInfoData? info, IReadOnlyList<IconifyIconData> icons)
    {
        Prefix = prefix;
        Info = info;
        Icons = icons;
    }

    public string Prefix { get; }
    public IconifyIconSetInfoData? Info { get; }

    /// <summary>The icons and the aliases, in the order of the file (icons first).</summary>
    public IReadOnlyList<IconifyIconData> Icons { get; }
}
