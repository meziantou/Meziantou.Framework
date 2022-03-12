using Xunit;

namespace Meziantou.Framework.ChromiumTracing.Tests;

public sealed class WriterTests
{
    [Fact]
    public async Task WriteEvents()
    {
        await using var writer = ChromiumTracingWriter.Create(Stream.Null);

        var eventTypes = typeof(ChromiumTracingWriter).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(ChromiumTracingEvent)));
        foreach (var eventType in eventTypes)
        {
            var instance = (ChromiumTracingEvent)Activator.CreateInstance(eventType);
            await writer.WriteEventAsync(instance);
        }
    }
}
