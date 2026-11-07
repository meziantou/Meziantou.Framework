namespace Meziantou.Framework.Csv.Fuzz;

/// <summary>A reader over a text whose reads return the number of characters a <see cref="ChunkSizer"/> gives, and complete synchronously.</summary>
internal sealed class ChunkedTextReader(string text, ChunkSizer sizer) : TextReader
{
    private int _position;

    public override int Peek() => _position < text.Length ? text[_position] : -1;

    public override int Read() => _position < text.Length ? text[_position++] : -1;

    public override int Read(char[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));

    public override int Read(Span<char> buffer)
    {
        var count = (int)Math.Min(Math.Min(buffer.Length, text.Length - _position), (long)sizer.Next());
        text.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override Task<int> ReadAsync(char[] buffer, int index, int count) => Task.FromResult(Read(buffer.AsSpan(index, count)));

    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));
}
