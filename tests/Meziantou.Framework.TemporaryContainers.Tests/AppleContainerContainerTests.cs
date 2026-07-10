namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class AppleContainerContainerTests() : ContainerRuntimeTestsBase(ContainerRuntime.AppleContainer)
{
    [Fact]
    public async Task PauseAsync_IsNotSupported()
    {
        await using var container = await StartWithRetryAsync(CreateHttpServerDefinition());
        await Assert.ThrowsAsync<NotSupportedException>(() => container.PauseAsync(XunitCancellationToken));
    }
}
