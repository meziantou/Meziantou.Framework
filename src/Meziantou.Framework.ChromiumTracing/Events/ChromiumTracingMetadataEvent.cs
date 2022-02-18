using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing
{
    public sealed class ChromiumTracingMetadataEvent : ChromiumTracingEvent
    {
        [JsonPropertyName("ph")]
        public override string Type => "M";

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
}
