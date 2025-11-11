using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

/// <summary>Represents a metadata event that adds process and thread names for better visualization.</summary>
/// <example>
/// <code>
/// await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ProcessName(
///     Environment.ProcessId,
///     "My Application"
/// ));
///
/// await writer.WriteEventAsync(ChromiumTracingMetadataEvent.ThreadName(
///     Environment.ProcessId,
///     Environment.CurrentManagedThreadId,
///     "Main Thread"
/// ));
/// </code>
/// </example>
public sealed class ChromiumTracingMetadataEvent : ChromiumTracingEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "M";

    /// <summary>Creates a metadata event that sets a process name.</summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="name">The name to assign to the process.</param>
    /// <returns>A metadata event that sets the process name.</returns>
    public static ChromiumTracingMetadataEvent ProcessName(int pid, string name)
    {
        return new ChromiumTracingMetadataEvent
        {
            ProcessId = pid,
            Name = "process_name",
            Arguments = new Dictionary<string, object?>(System.StringComparer.Ordinal)
            {
                { "name", name },
            },
        };
    }

    /// <summary>Creates a metadata event that sets process labels.</summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="labels">The labels to assign to the process.</param>
    /// <returns>A metadata event that sets the process labels.</returns>
    public static ChromiumTracingMetadataEvent ProcessLabels(int pid, params string[] labels)
    {
        return new ChromiumTracingMetadataEvent
        {
            ProcessId = pid,
            Name = "process_labels",
            Arguments = new Dictionary<string, object?>(System.StringComparer.Ordinal)
            {
                { "labels", string.Join(',', labels) },
            },
        };
    }

    /// <summary>Creates a metadata event that sets a process sort index for display ordering.</summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="index">The sort index.</param>
    /// <returns>A metadata event that sets the process sort index.</returns>
    public static ChromiumTracingMetadataEvent ProcessSortIndex(int pid, int index)
    {
        return new ChromiumTracingMetadataEvent
        {
            ProcessId = pid,
            Name = "process_sort_index",
            Arguments = new Dictionary<string, object?>(System.StringComparer.Ordinal)
            {
                { "sort_index", index },
            },
        };
    }

    /// <summary>Creates a metadata event that sets a thread name.</summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="tid">The thread ID.</param>
    /// <param name="name">The name to assign to the thread.</param>
    /// <returns>A metadata event that sets the thread name.</returns>
    public static ChromiumTracingMetadataEvent ThreadName(int pid, int tid, string name)
    {
        return new ChromiumTracingMetadataEvent
        {
            ProcessId = pid,
            ThreadId = tid,
            Name = "thread_name",
            Arguments = new Dictionary<string, object?>(System.StringComparer.Ordinal)
            {
                { "name", name },
            },
        };
    }

    /// <summary>Creates a metadata event that sets a thread sort index for display ordering.</summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="tid">The thread ID.</param>
    /// <param name="index">The sort index.</param>
    /// <returns>A metadata event that sets the thread sort index.</returns>
    public static ChromiumTracingMetadataEvent ThreadSortIndex(int pid, int tid, int index)
    {
        return new ChromiumTracingMetadataEvent
        {
            ProcessId = pid,
            ThreadId = tid,
            Name = "thread_sort_index",
            Arguments = new Dictionary<string, object?>(System.StringComparer.Ordinal)
            {
                { "sort_index", index },
            },
        };
    }
}
