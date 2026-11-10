namespace Meziantou.Framework.VectorGraphics;

/// <summary>The geometry of a <see cref="ScenePath"/>: a list of commands, as in SVG.</summary>
public sealed class PathData
{
    internal PathData(PathCommand[] commands)
    {
        Commands = commands;
    }

    /// <summary>Gets the commands of the path.</summary>
    public IReadOnlyList<PathCommand> Commands { get; }

    /// <summary>Gets a value that indicates whether the path has no commands.</summary>
    public bool IsEmpty => Commands.Count is 0;

    /// <summary>Returns the path as the value of the <c>d</c> attribute of an SVG <c>path</c>.</summary>
    /// <returns>The path data.</returns>
    public override string ToString() => SvgNumber.FormatPath(this, SvgRenderOptions.DefaultPrecision);
}
