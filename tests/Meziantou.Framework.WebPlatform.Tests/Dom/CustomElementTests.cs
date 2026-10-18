using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class CustomElementTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>A custom element that records its lifecycle callbacks.</summary>
    private sealed class LoggingElement : CustomElement
    {
        public LoggingElement(HtmlElement element, List<string> log)
            : base(element)
        {
            Log = log;
            Log.Add("constructed");
        }

        public List<string> Log { get; }

        protected internal override void ConnectedCallback() => Log.Add("connected");

        protected internal override void DisconnectedCallback() => Log.Add("disconnected");

        protected internal override void AdoptedCallback(Document oldDocument, Document newDocument) => Log.Add("adopted");

        protected internal override void AttributeChangedCallback(string name, string? oldValue, string? newValue, string? @namespace) => Log.Add($"attributeChanged {name} {oldValue ?? "null"} {newValue ?? "null"}");
    }

    private static CustomElementConstructor CreateLoggingConstructor(List<string> log, params string[] observedAttributes)
        => new(element => new LoggingElement(element, log)) { ObservedAttributes = observedAttributes };

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void Define_UpgradesTheElementsOfTheDocument()
    {
        var document = Load("<x-foo id=a title=t></x-foo>");
        var element = (HtmlElement)document.GetElementById("a")!;
        Assert.False(element.Matches(":defined"));
        Assert.Null(element.CustomElement);

        var log = new List<string>();
        document.DefaultView!.CustomElements.Define("x-foo", CreateLoggingConstructor(log, "title"));

        Assert.True(element.Matches(":defined"));
        Assert.IsType<LoggingElement>(element.CustomElement);
        Assert.Same(element, element.CustomElement!.Element);
        Assert.Equal(["constructed", "attributeChanged title null t", "connected"], log);
    }

    [Fact]
    public void CreateElement_ConstructsTheElementSynchronously()
    {
        var document = Load("");
        var log = new List<string>();
        var registry = document.DefaultView!.CustomElements;
        registry.Define("x-foo", CreateLoggingConstructor(log, "title"));

        var element = (HtmlElement)document.CreateElement("x-foo");
        Assert.Equal(["constructed"], log);
        Assert.Same(registry, element.CustomElementRegistry);

        // The callbacks are invoked before the methods return
        element.SetAttribute("title", "a");
        element.SetAttribute("lang", "en");
        Assert.Equal(["constructed", "attributeChanged title null a"], log);

        document.Body!.AppendChild(element);
        Assert.Equal(["constructed", "attributeChanged title null a", "connected"], log);

        element.Remove();
        Assert.Equal(["constructed", "attributeChanged title null a", "connected", "disconnected"], log);
    }

    [Fact]
    public void HtmlParser_ConstructsTheElementsWithTheRegistryOfTheOptions()
    {
        var log = new List<string>();
        var registry = new CustomElementRegistry();
        registry.Define("x-foo", CreateLoggingConstructor(log, "class"));

        var document = HtmlDocumentParser.ParseDocument("<body><x-foo class=c></x-foo>", new HtmlParserOptions { CustomElementRegistry = registry });

        Assert.Same(registry, document.CustomElementRegistry);
        var element = (HtmlElement)document.QuerySelector("x-foo")!;
        Assert.IsType<LoggingElement>(element.CustomElement);
        Assert.Equal(["constructed", "attributeChanged class null c", "connected"], log);
    }

    [Fact]
    public void Initialize_UpgradesTheElementsOfADocumentWithoutRegistry()
    {
        var document = HtmlDocumentParser.ParseDocument("<x-foo></x-foo><x-bar></x-bar>");
        Assert.Null(document.CustomElementRegistry);

        var log = new List<string>();
        var registry = new CustomElementRegistry();
        registry.Define("x-foo", CreateLoggingConstructor(log));
        registry.Initialize(document);

        Assert.Same(registry, document.CustomElementRegistry);
        Assert.Equal(["constructed", "connected"], log);
        Assert.IsType<LoggingElement>(((HtmlElement)document.QuerySelector("x-foo")!).CustomElement);
        Assert.Null(((HtmlElement)document.QuerySelector("x-bar")!).CustomElement);
        Assert.Same(registry, document.QuerySelector("x-bar")!.CustomElementRegistry);
    }

    [Fact]
    public void AdoptNode_InvokesAdoptedCallback()
    {
        var document = Load("");
        var log = new List<string>();
        document.DefaultView!.CustomElements.Define("x-foo", CreateLoggingConstructor(log));
        var element = document.CreateElement("x-foo");
        document.Body!.AppendChild(element);

        var other = document.Implementation.CreateHtmlDocument("");
        other.Body!.AppendChild(element);

        Assert.Equal(["constructed", "connected", "disconnected", "adopted", "connected"], log);
    }

    [Fact]
    public void MoveBefore_DefaultConnectedMoveCallbackInvokesDisconnectedAndConnected()
    {
        var document = Load("<div id=a></div><div id=b></div>");
        var log = new List<string>();
        document.DefaultView!.CustomElements.Define("x-foo", CreateLoggingConstructor(log));
        var element = document.CreateElement("x-foo");
        document.GetElementById("a")!.AppendChild(element);

        document.GetElementById("b")!.MoveBefore(element, child: null);

        Assert.Equal(["constructed", "connected", "disconnected", "connected"], log);
    }

    [Fact]
    public async Task WhenDefined_CompletesWhenTheElementIsDefined()
    {
        var registry = new CustomElementRegistry();
        var task = registry.WhenDefined("x-foo");
        Assert.False(task.IsCompleted);
        Assert.Same(task, registry.WhenDefined("x-foo"));

        var constructor = new CustomElementConstructor(element => new LoggingElement(element, []));
        registry.Define("x-foo", constructor);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(constructor, await task);
        Assert.Same(constructor, registry.Get("x-foo"));
        Assert.Equal("x-foo", registry.GetName(constructor));
        Assert.Null(registry.Get("x-bar"));
        Assert.True(registry.WhenDefined("not valid").IsFaulted);
    }

    [Fact]
    public void Define_ValidatesTheName()
    {
        var registry = new CustomElementRegistry();
        var constructor = new CustomElementConstructor(element => new LoggingElement(element, []));
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => registry.Define("div", constructor)).Name);

        registry.Define("x-foo", constructor);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => registry.Define("x-foo", new CustomElementConstructor(element => new LoggingElement(element, [])))).Name);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => registry.Define("x-bar", constructor)).Name);

        // A scoped registry cannot define customized built-in elements
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => registry.Define("x-button", new CustomElementConstructor(element => new LoggingElement(element, [])), new ElementDefinitionOptions { Extends = "button" })).Name);
    }

    [Fact]
    public void CustomizedBuiltInElement_UsesTheInterfaceOfTheExtendedElement()
    {
        var document = Load("<button is=x-button id=b></button>");
        var log = new List<string>();
        document.DefaultView!.CustomElements.Define("x-button", CreateLoggingConstructor(log), new ElementDefinitionOptions { Extends = "button" });

        var button = (HtmlButtonElement)document.GetElementById("b")!;
        Assert.IsType<LoggingElement>(button.CustomElement);

        var created = (HtmlElement)document.CreateElement("button", new ElementCreationOptions { Is = "x-button" });
        Assert.IsType<HtmlButtonElement>(created);
        Assert.IsType<LoggingElement>(created.CustomElement);
        Assert.Equal("<button is=\"x-button\"></button>", created.OuterHtml);
    }

    [Fact]
    public void ConstructorException_IsReportedAndTheElementFails()
    {
        var reported = new List<ScriptError>();
        var context = _contexts.Create(new BrowsingContextOptions { UnhandledScriptError = (_, error) => reported.Add(error) });
        var document = context.Navigate("https://example.com/", _ => "");
        _contexts.EventLoop.RunUntilIdle();
        document.DefaultView!.CustomElements.Define("x-foo", new CustomElementConstructor(_ => throw new InvalidOperationException("boom")));

        var element = document.CreateElement("x-foo");
        Assert.IsType<HtmlUnknownElement>(element);
        Assert.False(element.Matches(":defined"));

        Assert.Equal("boom", Assert.Single(reported).Message);
        Assert.IsType<InvalidOperationException>(reported[0].Error);
    }

    private sealed class Counter : CustomElement
    {
        public Counter(HtmlElement element)
            : base(element)
        {
            Internals = element.AttachInternals();
        }

        public ElementInternals Internals { get; }

        public List<HtmlFormElement?> Forms { get; } = [];

        public List<bool> DisabledStates { get; } = [];

        public int ResetCount { get; private set; }

        protected internal override void FormAssociatedCallback(HtmlFormElement? form) => Forms.Add(form);

        protected internal override void FormDisabledCallback(bool disabled) => DisabledStates.Add(disabled);

        protected internal override void FormResetCallback() => ResetCount++;
    }

    [Fact]
    public void FormAssociatedCustomElement_ParticipatesInTheForm()
    {
        var document = Load("<form id=f><x-counter name=count></x-counter><input name=text value=v></form>");
        document.DefaultView!.CustomElements.Define("x-counter", new CustomElementConstructor(element => new Counter(element)) { FormAssociated = true });

        var form = (HtmlFormElement)document.GetElementById("f")!;
        var element = (HtmlElement)document.QuerySelector("x-counter")!;
        var counter = (Counter)element.CustomElement!;
        Assert.Same(form, counter.Internals.Form);
        Assert.Equal([form], counter.Forms);
        Assert.Equal(2u, form.Elements.Length);

        counter.Internals.SetFormValue("42");
        var entries = new FormData(form).Select(e => $"{e.Key}={e.Value}").ToList();
        Assert.Equal(["count=42", "text=v"], entries);

        counter.Internals.SetValidity(new ValidityStateFlags { ValueMissing = true }, "required");
        Assert.False(counter.Internals.Validity.Valid);
        Assert.Equal("required", counter.Internals.ValidationMessage);
        Assert.False(form.CheckValidity());
        Assert.True(element.Matches(":invalid"));

        element.SetAttribute("disabled", "");
        Assert.Equal([true], counter.DisabledStates);
        Assert.True(element.Matches(":disabled"));

        form.Reset();
        Assert.Equal(1, counter.ResetCount);

        element.Remove();
        Assert.Null(counter.Internals.Form);
        Assert.Equal([form, null], counter.Forms);
    }

    [Fact]
    public void CustomStates_AreMatchedByTheStatePseudoClass()
    {
        var document = Load("");
        document.DefaultView!.CustomElements.Define("x-counter", new CustomElementConstructor(element => new Counter(element)));
        var element = (HtmlElement)document.CreateElement("x-counter");
        var states = ((Counter)element.CustomElement!).Internals.States;

        Assert.False(element.Matches(":state(checked)"));
        states.Add("checked");
        Assert.True(element.Matches(":state(checked)"));
        Assert.True(states.Has("checked"));
        Assert.Equal(["checked"], states);
        Assert.True(states.Delete("checked"));
        Assert.False(element.Matches(":state(checked)"));
    }

    [Fact]
    public void AttachInternals_ThrowsForElementsThatAreNotCustomElements()
    {
        var document = Load("<x-foo id=a></x-foo>");
        var element = (HtmlElement)document.GetElementById("a")!;
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(element.AttachInternals).Name);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(((HtmlElement)document.CreateElement("div")).AttachInternals).Name);
    }

    [Fact]
    public void Clone_UpgradesTheCopy()
    {
        var document = Load("");
        var log = new List<string>();
        document.DefaultView!.CustomElements.Define("x-foo", CreateLoggingConstructor(log, "title"));
        var element = document.CreateElement("x-foo");
        element.SetAttribute("title", "a");
        log.Clear();

        var copy = (HtmlElement)element.CloneNode();
        Assert.IsType<LoggingElement>(copy.CustomElement);
        Assert.Equal(["constructed", "attributeChanged title null a"], log);
    }

    [Fact]
    public void InnerHtml_UpgradesTheElementsAfterTheyAreInserted()
    {
        var document = Load("<div id=container></div>");
        var log = new List<string>();
        document.DefaultView!.CustomElements.Define("x-foo", CreateLoggingConstructor(log));

        document.GetElementById("container")!.InnerHtml = "<x-foo></x-foo>";

        Assert.Equal(["constructed", "connected"], log);
    }
}
