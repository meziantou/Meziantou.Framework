using Meziantou.AspNetCore.Testing.Components;
using Meziantou.AspNetCore.Testing.Components.JSInterop;
using Meziantou.AspNetCore.Testing.Tests.TestComponents;
using Meziantou.Framework.SnapshotTesting;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using static Meziantou.AspNetCore.Testing.Expectations;

namespace Meziantou.AspNetCore.Testing.Tests;

public sealed class ComponentTestHostTests
{
    [Fact]
    public async Task Counter_ClickIncrementsTheCount()
    {
        #region ComponentTestHost_Counter
        await using var host = new ComponentTestHost();
        var counter = await host.RenderAsync<Counter>(parameters => parameters.Add(c => c.IncrementAmount, 2));

        // A real click: the pointer moves to the center of the button, the hit testing finds the button
        await counter.Find("button").ClickAsync();
        await counter.GetByRole("button", "Click me").ClickAsync();

        Assert.Equal("Current count: 4", counter.GetByRole("status").TextContent);
        Assert.Equal("""
            <p role="status">Current count: 4</p>
            <button class="btn">Click me</button>
            """, counter.Markup);
        #endregion
    }

    [Fact]
    public async Task Counter_RenderCountAndSetParameters()
    {
        await using var host = new ComponentTestHost();
        var counter = await host.RenderAsync<Counter>();
        Assert.Equal(1, counter.RenderCount);

        await counter.SetParametersAsync(parameters => parameters.Add(c => c.IncrementAmount, 10));
        await counter.Find("button").ClickAsync();

        Assert.Equal("Current count: 10", counter.Find("p").TextContent);
        Assert.Equal(3, counter.RenderCount);
    }

    [Fact]
    public async Task TodoList_TypeAndKeyedReorder()
    {
        await using var host = new ComponentTestHost();
        var todo = await host.RenderAsync<TodoList>(parameters => parameters.Add(c => c.Items, ["a", "b", "c"]));
        var firstItem = todo.FindAll("li")[0];

        await todo.Find("#new-item").TypeAsync("d");
        await host.Input.PressAsync("Enter");
        Assert.Equal(["a", "b", "c", "d"], todo.FindAll("li span").Select(span => span.TextContent));
        Assert.Equal("", todo.Find<HtmlInputElement>("#new-item").Value);

        // @key moves the existing elements
        await todo.GetByRole("button", "Reverse").ClickAsync();
        Assert.Equal(["d", "c", "b", "a"], todo.FindAll("li span").Select(span => span.TextContent));
        Assert.Same(firstItem, todo.FindAll("li")[3]);

        await todo.GetByRole("button", "Remove c").ClickAsync();
        Assert.Equal(["d", "b", "a"], todo.FindAll("li span").Select(span => span.TextContent));
    }

    [Fact]
    public async Task AsyncLoader_RenderShowsTheFirstRenderAndRenderAsyncWaits()
    {
        #region ComponentTestHost_Wait
        await using var host = new ComponentTestHost();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = host.Render<AsyncLoader>(parameters => parameters.Add(c => c.Load, () => completion.Task));
        Assert.Equal("Loading...", loader.Find("p").TextContent);

        // The assertion runs the event loop until the component renders the data
        completion.SetResult("Loaded");
        await Expect(loader.Locator(".data")).ToHaveTextAsync("Loaded");
        #endregion

        var loader2 = await host.RenderAsync<AsyncLoader>(parameters => parameters.Add(c => c.Load, async () =>
        {
            await Task.Delay(10);
            return "Delayed";
        }));
        Assert.Equal("Delayed", loader2.Find(".data").TextContent);
    }

    [Fact]
    public async Task InputEvents_PointerTouchWheelKeyboard()
    {
        await using var host = new ComponentTestHost();
        var component = await host.RenderAsync<InputEvents>();
        var area = component.Find("#area");

        await area.HoverAsync();
        await area.ClickAsync(new ClickOptions { Position = new ElementPosition(10, 20) });
        await area.TapAsync();
        await host.Input.WheelAsync(area, 0, 50);
        await host.Input.RightClickAsync(area);
        await component.Find("#keys").TypeAsync("a");
        await host.Input.PressAsync("b", "Control");

        Assert.Equal("mouseover,pointerdown:mouse:10:20,pointerdown:touch:100:50,touchstart:1,wheel:50,pointerdown:mouse:100:50,contextmenu:2,keydown:a:False,keydown:Control:True,keydown:b:True", component.Find("#log").TextContent);
    }

    [Fact]
    public async Task Overlay_CoveredElementCannotBeClicked()
    {
        await using var host = new ComponentTestHost(new ComponentTestHostOptions { DefaultTimeout = TimeSpan.FromMilliseconds(200) });
        var component = await host.RenderAsync<Overlay>();

        var exception = await Assert.ThrowsAsync<ElementNotInteractableException>(() => component.Find("#covered").ClickAsync());
        Assert.Contains("<div id=\"overlay\"> receives the input", exception.Message);

        await component.Find("#overlay").ClickAsync();
        await component.Find("#covered").ClickAsync();
        Assert.True(component.Instance.Clicked);
    }

    [Fact]
    public async Task PersonForm_BindValidateAndSubmit()
    {
        #region ComponentTestHost_Forms
        await using var host = new ComponentTestHost();
        var form = await host.RenderAsync<PersonForm>();

        await form.GetByRole("button", "Save").ClickAsync();
        Assert.False(form.Instance.Submitted);
        Assert.Equal("The Name field is required.", form.Find(".validation-message").TextContent);

        await form.GetByLabel("Name").FillAsync("Ada");
        await form.GetByLabel("Subscribe").CheckAsync();
        await form.GetByLabel("Country").SelectOptionAsync("fr");
        await form.GetByRole("button", "Save").ClickAsync();

        Assert.True(form.Instance.Submitted);
        Assert.Equal("Ada", form.Instance.Person.Name);
        Assert.True(form.Instance.Person.Subscribe);
        Assert.Equal("fr", form.Instance.Person.Country);
        // The snapshot shows the state of the field: Blazor sets the value as a property, not as an attribute
        Assert.Equal("""<input id="name" name="Person.Name" class="modified valid" value="Ada">""", form.GetByLabel("Name").GetMarkup());
        #endregion
    }

    [Fact]
    public async Task JSInterop_MockedCallsAndBuiltInFocus()
    {
        #region ComponentTestHost_JSInterop
        await using var host = new ComponentTestHost();
        host.JSInterop.Setup("getGreeting", invocation => "Hello " + invocation.Arguments[1]);
        var component = await host.RenderAsync<FocusInput>();

        await component.GetByRole("button", "Focus").ClickAsync();
        Assert.Same(component.Find("#focus-target"), host.Document.ActiveElement);

        await component.GetByRole("button", "Call JS").ClickAsync();
        Assert.Equal("Hello Ada", component.Find("#result").TextContent);

        var invocation = Assert.Single(host.JSInterop.GetInvocations("getGreeting"));
        Assert.Same(component.Find("#focus-target"), host.GetElement((ElementReference)invocation.Arguments[0]!));
        #endregion
    }

    [Fact]
    public async Task JSInterop_StrictModeThrows()
    {
        await using var host = new ComponentTestHost();
        host.JSInterop.Mode = JSInteropMode.Strict;
        var component = await host.RenderAsync<FocusInput>();

        var exception = await Assert.ThrowsAsync<ComponentException>(() => component.GetByRole("button", "Call JS").ClickAsync());
        Assert.IsType<UnplannedJSInvocationException>(exception.InnerException);
    }

    [Fact]
    public async Task JSInterop_CallsTheScriptsOfThePage()
    {
        #region ComponentTestHost_PageJavaScript
        await using var host = new ComponentTestHost(new ComponentTestHostOptions
        {
            EnableJavaScript = true,
            HostPage = """
                <!DOCTYPE html>
                <html>
                <head>
                <script>
                  window.getGreeting = async (element, name) => element.id + ': Hello ' + name;
                </script>
                </head>
                <body></body>
                </html>
                """,
        });
        var component = await host.RenderAsync<FocusInput>();

        await component.GetByRole("button", "Call JS").ClickAsync();
        Assert.Equal("focus-target: Hello Ada", component.Find("#result").TextContent);
        #endregion
    }

    [Fact]
    public async Task Router_NavigatesWithLinksAndNavigationManager()
    {
        #region ComponentTestHost_Routing
        await using var host = new ComponentTestHost();
        var app = await host.RenderAsync<App>();
        Assert.Equal("Home", app.Find("h1").TextContent);
        Assert.Equal("active", app.GetByText("Home link").GetAttribute("class"));

        await app.GetByRole("link", "About").ClickAsync();
        Assert.Equal("About", app.Find("h1").TextContent);
        Assert.Equal("https://localhost/about", host.NavigationManager.Uri);
        Assert.Equal("https://localhost/about", host.BrowsingContext.Window.Location.Href);

        await app.GetByRole("button", "Back").ClickAsync();
        Assert.Equal("Home", app.Find("h1").TextContent);

        await host.NavigateAsync("/missing");
        Assert.Equal("Not found", app.Find("p").TextContent);
        #endregion
    }

    [Fact]
    public async Task StyledCard_ComputedStyleAndScreenshot()
    {
        #region ComponentTestHost_Snapshots
        await using var host = new ComponentTestHost(new ComponentTestHostOptions
        {
            Viewport = new ViewportOptions { Width = 400, Height = 300 },
            StyleSheets = { "body { margin: 0 } .card { width: 200px; padding: 10px; background: rgb(0, 128, 0); color: white }" },
        });
        var card = await host.RenderAsync<StyledCard>(parameters => parameters
            .Add(c => c.Title, "Title")
            .AddChildContent("Some <em>content</em>"));

        var element = card.Find(".card");
        Assert.Equal("rgb(0, 128, 0)", host.BrowsingContext.Window.GetComputedStyle(element).BackgroundColor);
        Assert.Equal(220, element.GetBoundingClientRect().Width);

        var png = card.TakeScreenshotAsPng();
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Take(4));

        Assert.Equal("""
            - heading "Title" [level=2]
            - paragraph:
              - text: Some
              - emphasis: content
            """, card.GetAccessibilitySnapshot());
        #endregion
    }

    [Fact]
    public async Task StyledCard_ScreenshotMatchesTheSnapshot()
    {
        #region ComponentTestHost_ScreenshotSnapshot
        await using var host = new ComponentTestHost(new ComponentTestHostOptions
        {
            Viewport = new ViewportOptions { Width = 400, Height = 300 },
            StyleSheets = { "body { margin: 0 } .card { width: 200px; padding: 10px; background: rgb(0, 128, 0); color: white; font-family: sans-serif }" },
        });
        var card = await host.RenderAsync<StyledCard>(parameters => parameters
            .Add(c => c.Title, "Title")
            .AddChildContent("Some <em>content</em>"));

        // The first run writes __snapshots__/ComponentTestHostTests_StyledCard_ScreenshotMatchesTheSnapshot.actual.png: check it and
        // rename it to .verified.png (or run with SNAPSHOTTESTING_STRATEGY=Overwrite); the next runs compare the screenshot with it
        Snapshot.Validate(card.TakeScreenshotAsPng(), SnapshotType.Png);
        #endregion
    }

    [Fact]
    public async Task Markup_DiffsMarkupSvgAndChildComponents()
    {
        await using var host = new ComponentTestHost();
        var component = await host.RenderAsync<Markup>(parameters => parameters.Add(c => c.Children, [1, 2]));
        Assert.Equal("http://www.w3.org/2000/svg", component.Find("circle").NamespaceUri);
        Assert.Equal(2, component.FindComponents<Counter>().Count);

        await component.SetParametersAsync(parameters => parameters
            .Add(c => c.Html, "<i>italic</i>")
            .Add(c => c.Radius, 3)
            .Add(c => c.Children, [3]));
        Assert.Equal("""
            <div id="container">
              <i>italic</i>
              <svg width="10" height="10">
                <circle cx="5" cy="5" r="3"></circle>
              </svg>
              <p role="status">Current count: 0</p>
              <button class="btn">Click me</button>
            </div>
            """, component.Markup);

        await component.SetParametersAsync(parameters => parameters.Add(c => c.ShowMarkup, false));
        Assert.Equal("<svg", component.Find("#container").InnerHtml.Trim()[..4]);
        Assert.Equal(3, component.FindComponent<Counter>().Instance.IncrementAmount);
    }

    [Fact]
    public async Task EventModifiers_PreventDefaultAndStopPropagation()
    {
        await using var host = new ComponentTestHost();
        var component = await host.RenderAsync<EventModifiers>();

        // @onclick:stopPropagation: the handler of the parent does not run
        await component.Find("#stopped").ClickAsync();
        Assert.Equal((1, 0), (component.Instance.InnerClicks, component.Instance.OuterClicks));
        await component.Find("#bubbling").ClickAsync();
        Assert.Equal((2, 1), (component.Instance.InnerClicks, component.Instance.OuterClicks));

        // @onclick:preventDefault: the check box is not checked, the link is not followed
        await component.Find("#prevented").ClickAsync();
        Assert.False(component.Find<HtmlInputElement>("#prevented").Checked);
        await component.Find("#link").ClickAsync();
        Assert.Equal(1, component.Instance.LinkClicks);
        Assert.Equal("https://localhost/", host.Url);
    }

    [Fact]
    public async Task Services_AreInjected()
    {
        await using var host = new ComponentTestHost();
        host.Services.AddSingleton(new Greeting("Hello"));
        var component = await host.RenderAsync(builder =>
        {
            builder.OpenComponent<GreetingComponent>(0);
            builder.CloseComponent();
        });

        Assert.Equal("<p>Hello</p>", component.Markup);
    }

    [Fact]
    public async Task Dispose_RunsTheJavaScriptCallsOfTheDisposedServices()
    {
        // A service that releases a JavaScript object when it is disposed calls the page: the event loop of the page
        // must run while the host waits for the disposal
        var results = new List<string>();
        var host = new ComponentTestHost(new ComponentTestHostOptions
        {
            EnableJavaScript = true,
            HostPage = "<!DOCTYPE html><html><head><script>window.release = () => 'released';</script></head><body></body></html>",
        });
        host.Services.AddSingleton(provider => new JavaScriptDisposable(provider.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(), results));
        _ = host.ServiceProvider.GetRequiredService<JavaScriptDisposable>();

        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(["released"], results);
    }

    private sealed class JavaScriptDisposable(Microsoft.JSInterop.IJSRuntime jsRuntime, List<string> results) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => results.Add(await jsRuntime.InvokeAsync<string>("release", []));
    }

    private sealed record Greeting(string Text);

    private sealed class GreetingComponent : ComponentBase
    {
        [Inject]
        public Greeting Greeting { get; set; } = default!;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, Greeting.Text);
            builder.CloseElement();
        }
    }
}
