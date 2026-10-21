using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The tentative extensions of the templates that patch content (the declarative partial updates,
/// https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md): <c>buffer</c>, <c>sanitize</c>
/// and <c>src</c>.
/// </summary>
public sealed class TemplatePatchingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = new PatchLoader(),
            Features = features,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><div id=target><?start name=\"x\">old<?end></div>" + html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>Serves the content of the templates with a src attribute.</summary>
    private sealed class PatchLoader : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<i>fetched</i><script>document.title = 'fetched script'</script>") { Headers = [new("Content-Type", "text/html")] });
    }

    private static string Target(Document document) => document.GetElementById("target")!.InnerHtml;

    [Fact]
    public void Buffer_InsertsTheContentAtOnceWhenTheTemplateIsClosed()
    {
        var document = Load("<template for=x buffer><b>1</b><script>document.title = document.querySelectorAll('#target b').length + ' ' + !!document.querySelector('template')</script><b>2</b></template>");

        // The scripts of the content run when it is inserted, before the template is removed
        Assert.Equal("2 true", document.Title);
        Assert.StartsWith("<b>1</b><script>", Target(document), StringComparison.Ordinal);
        Assert.EndsWith("</script><b>2</b>", Target(document), StringComparison.Ordinal);
        Assert.Null(document.QuerySelector("template"));
    }

    [Fact]
    public void Sanitize_RemovesTheUnsafeContent()
    {
        var document = Load("<template for=x sanitize=SANITIZE><b onclick=alert(1)>1</b><script>document.title = 'ran'</script></template>");
        Assert.Equal("", document.Title);
        Assert.Equal("<b>1</b>", Target(document));
    }

    [Fact]
    public void Src_FetchesTheContent()
    {
        var document = Load("<template for=x src=patch.html><u>ignored</u></template>");
        Assert.Equal("fetched script", document.Title);
        Assert.StartsWith("<i>fetched</i><script>", Target(document), StringComparison.Ordinal);
        Assert.Null(document.QuerySelector("template"));
    }

    [Fact]
    public void DisabledFeature_IgnoresTheTentativeAttributes()
    {
        var document = Load("<template for=x buffer sanitize><b>1</b><script>document.title = 'buffer' in HTMLTemplateElement.prototype</script></template>", new WebPlatformFeatures { DeclarativePartialUpdatesEnabled = false });
        Assert.Equal("false", document.Title);
        Assert.StartsWith("<b>1</b><script>", Target(document), StringComparison.Ordinal);
    }
}
