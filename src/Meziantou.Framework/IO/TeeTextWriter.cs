using System.Text;

namespace Meziantou.Framework.IO;

public sealed class TeeTextWriter : TextWriter
{
    private readonly Lock _lock = new();

    public TextWriter Stream1 { get; }
    public TextWriter Stream2 { get; }

    public TeeTextWriter(TextWriter stream1, TextWriter stream2)
    {
        Stream1 = stream1;
        Stream2 = stream2;
    }

    public override Encoding Encoding => Stream1.Encoding;

    public override void Write(char value)
    {
        lock (_lock)
        {
            Stream1.Write(value);
            Stream2.Write(value);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (_lock)
        {
            Stream1.Write(buffer, index, count);
            Stream2.Write(buffer, index, count);
        }
    }

    public override void Write(string? value)
    {
        lock (_lock)
        {
            Stream1.Write(value);
            Stream2.Write(value);
        }
    }

    public override void Flush()
    {
        lock (_lock)
        {
            Stream1.Flush();
            Stream2.Flush();
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (disposing)
            {
                Stream1.Dispose();
                Stream2.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
