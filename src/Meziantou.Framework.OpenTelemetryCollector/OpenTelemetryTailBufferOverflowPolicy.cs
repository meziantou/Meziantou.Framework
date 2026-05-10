namespace Meziantou.Framework.OpenTelemetryCollector;

public enum OpenTelemetryTailBufferOverflowPolicy
{
    DropWholeTrace,
    DropOldestSpans,
    DropNewestSpans,
}
