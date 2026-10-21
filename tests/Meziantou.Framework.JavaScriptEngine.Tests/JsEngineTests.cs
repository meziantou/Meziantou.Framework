namespace Meziantou.Framework.JavaScriptEngine.Tests;

public sealed class JsEngineTests
{
    [Fact]
    public void CreateAndDispose()
    {
        using var engine = new JsEngine();
        Assert.NotNull(engine);
    }
}
