namespace Meziantou.Framework.Yaml;

internal sealed class StringLookAheadBuffer : ILookAheadBuffer
{
    private readonly string _value;

    public int Length { get { return _value.Length; } }

    public int Position { get; private set; }

    private bool IsOutside(int index)
    {
        return index >= _value.Length;
    }

    public bool EndOfInput { get { return IsOutside(Position); } }

    /// <summary>Gets the characters that are not consumed yet.</summary>
    public ReadOnlySpan<char> Remaining => IsOutside(Position) ? default : _value.AsSpan(Position);

    public StringLookAheadBuffer(string value)
    {
        this._value = value;
    }

    public char Peek(int offset)
    {
        int index = Position + offset;
        return IsOutside(index) ? '\0' : _value[index];
    }

    public void Skip(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The length must be positive.");
        }
        Position += length;
    }

    public void Cache(int length) { }
}