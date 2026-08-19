using System.Buffers;
using System.Diagnostics;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Defines a thread-local cache for <see cref="YamlSerializer"/> to store reusable <see cref="BufferWriterTextWriter"/> instances.
/// </summary>
internal static class BufferWriterTextWriterCache
{
    [ThreadStatic]
    private static ThreadLocalState? t_threadLocalState;

    public static BufferWriterTextWriter RentWriter(IBufferWriter<char> destination)
    {
        var state = t_threadLocalState ??= new ThreadLocalState();
        BufferWriterTextWriter writer;

        if (state.RentedWriters++ == 0)
        {
            // First YamlSerializer call in the stack -- initialize & return the cached instance.
            writer = state.Writer;
            writer.ConfigureForCacheReuse(destination);
        }
        else
        {
            // We're in a recursive YamlSerializer call -- return a fresh instance.
            writer = new BufferWriterTextWriter(destination);
        }

        return writer;
    }

    public static void ReturnWriter(BufferWriterTextWriter writer)
    {
        var state = t_threadLocalState;
        Debug.Assert(state is not null);

        writer.ResetAllStateForCacheReuse();

        var rentedWriters = --state.RentedWriters;
        Debug.Assert((rentedWriters == 0) == ReferenceEquals(state.Writer, writer));
    }

    private sealed class ThreadLocalState
    {
        public readonly BufferWriterTextWriter Writer = BufferWriterTextWriter.CreateEmptyInstanceForCaching();
        public int RentedWriters;
    }
}
