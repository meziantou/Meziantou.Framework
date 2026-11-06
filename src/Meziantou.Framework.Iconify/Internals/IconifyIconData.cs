namespace Meziantou.Framework.Internals;

/// <summary>An icon whose aliases and transformations are resolved.</summary>
internal sealed class IconifyIconData
{
    public IconifyIconData(string name, string body, double left, double top, double width, double height, bool hidden)
    {
        Name = name;
        Body = body;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Hidden = hidden;
    }

    public string Name { get; }
    public string Body { get; }
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }
    public bool Hidden { get; }
}
