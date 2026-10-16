namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The outlines of the distinct glyphs of a layout, recorded when the layout is created: the calls the font made to an
/// <see cref="OutlineSink"/>, with their coordinates in font units. Drawing a layout replays them, so that a glyph is
/// decoded once, not each time it is drawn.
/// </summary>
/// <remarks>
/// A sink that receives the recorded calls receives the calls of the font: the same commands with the same numbers, in
/// the same order. The outlines are immutable once recorded, as the layout that holds them.
/// </remarks>
internal sealed class RecordedOutlines
{
    private const byte MoveToCommand = 0;
    private const byte LineToCommand = 1;
    private const byte QuadraticToCommand = 2;
    private const byte CubicToCommand = 3;
    private const byte CloseCommand = 4;

    private readonly byte[] _commands;
    private readonly double[] _coordinates;

    // Where the commands and the coordinates of each outline start; one more entry for the end of the last one.
    private readonly int[] _commandStarts;
    private readonly int[] _coordinateStarts;

    private RecordedOutlines(byte[] commands, double[] coordinates, int[] commandStarts, int[] coordinateStarts)
    {
        _commands = commands;
        _coordinates = coordinates;
        _commandStarts = commandStarts;
        _coordinateStarts = coordinateStarts;
    }

    /// <summary>Sends a recorded outline to a sink.</summary>
    public void Replay(int outline, OutlineSink sink)
    {
        var commands = _commands.AsSpan(_commandStarts[outline], _commandStarts[outline + 1] - _commandStarts[outline]);
        var coordinates = _coordinates.AsSpan(_coordinateStarts[outline], _coordinateStarts[outline + 1] - _coordinateStarts[outline]);
        var index = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case MoveToCommand:
                    sink.MoveTo(coordinates[index], coordinates[index + 1]);
                    index += 2;
                    break;

                case LineToCommand:
                    sink.LineTo(coordinates[index], coordinates[index + 1]);
                    index += 2;
                    break;

                case QuadraticToCommand:
                    sink.QuadraticTo(coordinates[index], coordinates[index + 1], coordinates[index + 2], coordinates[index + 3]);
                    index += 4;
                    break;

                case CubicToCommand:
                    sink.CubicTo(coordinates[index], coordinates[index + 1], coordinates[index + 2], coordinates[index + 3], coordinates[index + 4], coordinates[index + 5]);
                    index += 6;
                    break;

                default:
                    sink.Close();
                    break;
            }
        }
    }

    /// <summary>Records outlines one after the other, and measures each one with a <see cref="GlyphBoundsSink"/>.</summary>
    internal sealed class Recorder : OutlineSink
    {
        private readonly List<byte> _commands = [];
        private readonly List<double> _coordinates = [];
        private readonly List<int> _commandStarts = [0];
        private readonly List<int> _coordinateStarts = [0];

        /// <summary>Gets the bounds of the outline being recorded.</summary>
        public GlyphBoundsSink Bounds { get; } = new();

        /// <summary>Starts an outline.</summary>
        public void Begin() => Bounds.Reset();

        /// <summary>Ends the outline being recorded.</summary>
        /// <returns>Its index.</returns>
        public int End()
        {
            _commandStarts.Add(_commands.Count);
            _coordinateStarts.Add(_coordinates.Count);
            return _commandStarts.Count - 2;
        }

        public RecordedOutlines Build() => new([.. _commands], [.. _coordinates], [.. _commandStarts], [.. _coordinateStarts]);

        public override void MoveTo(double x, double y)
        {
            _commands.Add(MoveToCommand);
            _coordinates.Add(x);
            _coordinates.Add(y);
            Bounds.MoveTo(x, y);
        }

        public override void LineTo(double x, double y)
        {
            _commands.Add(LineToCommand);
            _coordinates.Add(x);
            _coordinates.Add(y);
            Bounds.LineTo(x, y);
        }

        public override void QuadraticTo(double controlX, double controlY, double x, double y)
        {
            _commands.Add(QuadraticToCommand);
            _coordinates.Add(controlX);
            _coordinates.Add(controlY);
            _coordinates.Add(x);
            _coordinates.Add(y);
            Bounds.QuadraticTo(controlX, controlY, x, y);
        }

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
        {
            _commands.Add(CubicToCommand);
            _coordinates.Add(control1X);
            _coordinates.Add(control1Y);
            _coordinates.Add(control2X);
            _coordinates.Add(control2Y);
            _coordinates.Add(x);
            _coordinates.Add(y);
            Bounds.CubicTo(control1X, control1Y, control2X, control2Y, x, y);
        }

        public override void Close()
        {
            _commands.Add(CloseCommand);
            Bounds.Close();
        }
    }
}
