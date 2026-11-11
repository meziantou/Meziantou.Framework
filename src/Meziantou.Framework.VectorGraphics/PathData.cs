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

    /// <summary>Returns the path moved by an offset. The radii and the rotations of the arcs do not change.</summary>
    /// <param name="offsetX">The offset along x.</param>
    /// <param name="offsetY">The offset along y.</param>
    /// <returns>The moved path.</returns>
    public PathData Translate(double offsetX, double offsetY)
    {
        var commands = new PathCommand[Commands.Count];
        for (var i = 0; i < commands.Length; i++)
        {
            var command = Commands[i];
            commands[i] = command.Kind switch
            {
                PathCommandKind.MoveTo or PathCommandKind.LineTo or PathCommandKind.ArcTo => command with { X = command.X + offsetX, Y = command.Y + offsetY },
                PathCommandKind.QuadraticTo => command with { X = command.X + offsetX, Y = command.Y + offsetY, X1 = command.X1 + offsetX, Y1 = command.Y1 + offsetY },
                PathCommandKind.CubicTo => command with { X = command.X + offsetX, Y = command.Y + offsetY, X1 = command.X1 + offsetX, Y1 = command.Y1 + offsetY, X2 = command.X2 + offsetX, Y2 = command.Y2 + offsetY },
                _ => command,
            };
        }

        return new(commands);
    }

    /// <summary>Returns the path as the value of the <c>d</c> attribute of an SVG <c>path</c>.</summary>
    /// <returns>The path data.</returns>
    public override string ToString() => SvgNumber.FormatPath(this, SvgRenderOptions.DefaultPrecision);
}
