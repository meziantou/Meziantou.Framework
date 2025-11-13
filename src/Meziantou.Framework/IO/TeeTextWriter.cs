namespace Meziantou.Framework.IO;

/// <summary>A text writer that writes to two underlying text writers simultaneously.</summary>
/// <example>
/// <code>
/// using var file = new StreamWriter("log.txt");
/// using var tee = new TeeTextWriter(Console.Out, file);
/// tee.WriteLine("This goes to both console and file");
/// </code>
/// </example>
#if NET9_0_OR_GREATER
[Obsolete("Use TextWriter.CreateBroadcasting", DiagnosticId = "MEZ_NET9")]
#endif
public sealed class TeeTextWriter : TextWriter
{
    private readonly Lock _lock = new();

    /// <summary>Gets the first text writer.</summary>
    public TextWriter Stream1 { get; }

    /// <summary>Gets the second text writer.</summary>
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
