namespace Meziantou.Framework.Templating;

internal sealed class TextReaderWithPosition : TextReader
{
    private readonly TextReader _reader;
    private bool _previousIsCarriageReturn;

    public int Line { get; private set; } = 1;
    public int Column { get; private set; } = 1;
    public int Index { get; private set; }

    public int PreviousLine { get; private set; } = 1;
    public int PreviousColumn { get; private set; } = 1;
    public int PreviousIndex { get; private set; }

    public TextReaderWithPosition(TextReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public override int Peek()
    {
        return _reader.Peek();
    }

    public override int Read()
    {
        PreviousLine = Line;
        PreviousColumn = Column;
        PreviousIndex = Index;

        var read = _reader.Read();
        if (read >= 0)
        {
            Index++;

            var c = (char)read;
            if (c == '\r')
            {
                Line++;
                Column = 1;
                _previousIsCarriageReturn = true;
            }
            else if (c == '\n')
            {
                if (!_previousIsCarriageReturn)
                {
                    Line++;
                    Column = 1;
                }

                _previousIsCarriageReturn = false;
            }
            else
            {
                Column++;
                _previousIsCarriageReturn = false;
            }
        }

        return read;
    }
}
