using System.Buffers;
using System.Buffers.Text;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeWriter
{
    private readonly IBufferWriter<byte> _writer;
    private readonly List<ContainerState> _containers = [];
    private bool _hasWrittenRootValue;
    private bool _isComplete;

    public BencodeWriter(IBufferWriter<byte> writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void WriteInteger(long value)
    {
        var rootValueWasCompleted = StartValue();

        WriteByte((byte)'i');
        WriteInt64(value);
        WriteByte((byte)'e');

        if (rootValueWasCompleted)
        {
            _isComplete = true;
        }
    }

    public void WriteString(ReadOnlyMemory<byte> value)
    {
        WriteString(value.Span);
    }

    public void WriteString(ReadOnlySpan<byte> value)
    {
        var rootValueWasCompleted = StartValue();
        WriteStringCore(value);

        if (rootValueWasCompleted)
        {
            _isComplete = true;
        }
    }

    public void WriteUtf8String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteString(Encoding.UTF8.GetBytes(value));
    }

    public void WriteStartList()
    {
        StartValue();
        WriteByte((byte)'l');
        _containers.Add(new ContainerState(ContainerKind.List, ExpectingDictionaryKey: false));
    }

    public void WriteEndList()
    {
        var state = PopContainer(ContainerKind.List);
        WriteByte((byte)'e');
        CompleteRootContainerIfNeeded(state);
    }

    public void WriteStartDictionary()
    {
        StartValue();
        WriteByte((byte)'d');
        _containers.Add(new ContainerState(ContainerKind.Dictionary, ExpectingDictionaryKey: true));
    }

    public void WriteUtf8Key(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        WriteKey(Encoding.UTF8.GetBytes(key));
    }

    public void WriteKey(ReadOnlySpan<byte> key)
    {
        var state = GetCurrentContainer();
        if (state.Kind != ContainerKind.Dictionary)
            throw new InvalidOperationException("Dictionary keys can only be written while inside a dictionary.");

        if (!state.ExpectingDictionaryKey)
            throw new InvalidOperationException("Cannot write a dictionary key while expecting a value.");

        WriteStringCore(key);
        _containers[^1] = state with { ExpectingDictionaryKey = false };
    }

    public void WriteEndDictionary()
    {
        var state = PopContainer(ContainerKind.Dictionary);
        if (!state.ExpectingDictionaryKey)
            throw new InvalidOperationException("Cannot end a dictionary while expecting a value for the last key.");

        WriteByte((byte)'e');
        CompleteRootContainerIfNeeded(state);
    }

    public void Complete()
    {
        if (_isComplete)
            return;

        if (!_hasWrittenRootValue)
            throw new InvalidOperationException("Cannot complete bencode writing because no root value has been written.");

        if (_containers.Count != 0)
            throw new InvalidOperationException("Cannot complete bencode writing while containers are not closed.");

        _isComplete = true;
    }

    private bool StartValue()
    {
        if (_isComplete)
            throw new InvalidOperationException("A complete bencode value has already been written.");

        if (_containers.Count == 0)
        {
            if (_hasWrittenRootValue)
                throw new InvalidOperationException("Only one root bencode value can be written.");

            _hasWrittenRootValue = true;
            return true;
        }

        var state = _containers[^1];
        if (state.Kind == ContainerKind.Dictionary)
        {
            if (state.ExpectingDictionaryKey)
                throw new InvalidOperationException("Cannot write a dictionary value before writing its key.");

            _containers[^1] = state with { ExpectingDictionaryKey = true };
        }

        return false;
    }

    private ContainerState PopContainer(ContainerKind expectedContainerKind)
    {
        if (_containers.Count == 0)
            throw new InvalidOperationException($"Cannot end a {expectedContainerKind.ToString().ToLowerInvariant()} because no container is open.");

        var state = _containers[^1];
        if (state.Kind != expectedContainerKind)
            throw new InvalidOperationException($"Cannot end a {expectedContainerKind.ToString().ToLowerInvariant()} while inside a {state.Kind.ToString().ToLowerInvariant()}.");

        _containers.RemoveAt(_containers.Count - 1);
        return state;
    }

    private ContainerState GetCurrentContainer()
    {
        if (_containers.Count == 0)
            throw new InvalidOperationException("No container is currently open.");

        return _containers[^1];
    }

    private void CompleteRootContainerIfNeeded(ContainerState state)
    {
        if (_containers.Count != 0)
            return;

        if (state.Kind is ContainerKind.Dictionary && !state.ExpectingDictionaryKey)
            throw new InvalidOperationException("Cannot complete a dictionary with a missing value.");

        _isComplete = true;
    }

    private void WriteStringCore(ReadOnlySpan<byte> value)
    {
        WriteInt32(value.Length);
        WriteByte((byte)':');
        WriteBytes(value);
    }

    private void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[16];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written))
            throw new InvalidOperationException("Unable to write integer value.");

        WriteBytes(buffer[..written]);
    }

    private void WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[32];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written))
            throw new InvalidOperationException("Unable to write integer value.");

        WriteBytes(buffer[..written]);
    }

    private void WriteByte(byte value)
    {
        var span = _writer.GetSpan(1);
        span[0] = value;
        _writer.Advance(1);
    }

    private void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        var span = _writer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _writer.Advance(bytes.Length);
    }

    private enum ContainerKind
    {
        List,
        Dictionary,
    }

    private readonly record struct ContainerState(ContainerKind Kind, bool ExpectingDictionaryKey);
}
