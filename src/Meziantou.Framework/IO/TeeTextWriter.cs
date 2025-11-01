namespace Meziantou.Framework.IO;

/// <summary>
/// A <see cref="TextWriter"/> that writes to two underlying text writers simultaneously.
/// </summary>
#if NET9_0_OR_GREATER
[Obsolete("Use TextWriter.CreateBroadcasting", DiagnosticId = "MEZ_NET9")]
#endif
public sealed class TeeTextWriter : TextWriter
{
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets the first underlying text writer.
    /// </summary>
    public TextWriter Stream1 { get; }

    /// <summary>
    /// Gets the second underlying text writer.
    /// </summary>
    public TextWriter Stream2 { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TeeTextWriter"/> class with two underlying text writers.
    /// </summary>
    /// <param name="stream1">The first text writer.</param>
    /// <param name="stream2">The second text writer.</param>
    public TeeTextWriter(TextWriter stream1, TextWriter stream2)
    {
        Stream1 = stream1;
        Stream2 = stream2;
    }

    /// <inheritdoc/>
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
