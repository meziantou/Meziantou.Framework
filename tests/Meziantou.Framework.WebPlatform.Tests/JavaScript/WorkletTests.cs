using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>
/// The worklets (https://html.spec.whatwg.org/multipage/worklets.html) and the global scopes of the paint worklet
/// (https://drafts.css-houdini.org/css-paint-api-1/): the scripts of the global scopes report their results with the console.
/// </summary>
public sealed class WorkletTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly List<(GlobalScope Global, string Text)> _messages = [];
    private readonly RecordingLoader _loader = new();
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
            DocumentLoader = _loader,
            Features = features,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            ConsoleMessage = (global, message) => _messages.Add((global, message.Text)),
        });

        var document = context.Navigate("https://example.com/dir/page.html", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private void AddScript(string url, string source, params KeyValuePair<string, string>[] headers)
        => _loader.Resources[url] = new DocumentResource(source) { Headers = [new("Content-Type", "text/javascript"), .. headers] };

    private IEnumerable<string> WorkletMessages => _messages.Where(message => message.Global is WorkletGlobalScope).Select(message => message.Text);

    [Fact]
    public void AddModule_RunsTheModuleInEachGlobalScope()
    {
        AddScript("https://example.com/dir/paint.js", """
            import { value } from './lib.js';
            console.log([value, typeof registerPaint, typeof devicePixelRatio, typeof window, typeof document, typeof self, typeof fetch, typeof setTimeout,
              typeof CSSUnitValue, typeof PaintRenderingContext2D, typeof PaintSize, typeof Path2D, Object.prototype.toString.call(globalThis)].join());
            """);
        AddScript("https://example.com/dir/lib.js", "export const value = 42;");
        var document = Load("""
            <script>
              CSS.paintWorklet.addModule('paint.js', { credentials: 'omit' }).then(value => document.title = 'resolved ' + value + ' ' + (CSS.paintWorklet === CSS.paintWorklet) + ' ' + (CSS.paintWorklet instanceof Worklet));
            </script>
            """);

        Assert.Equal("resolved undefined true true", document.Title);
        Assert.Empty(_errors);

        // The paint worklet has two global scopes, which have no access to the DOM
        Assert.Equal(2, WorkletMessages.Count());
        Assert.All(WorkletMessages, message => Assert.Equal("42,function,number,undefined,undefined,undefined,undefined,undefined,function,function,function,function,[object PaintWorkletGlobalScope]", message));
        Assert.Equal(2, _messages.Select(message => message.Global).Distinct().Count());

        // The modules are fetched once for the worklet, as CORS requests of the document
        var requests = _loader.Requests.Where(request => request.Url.EndsWith(".js", StringComparison.Ordinal)).ToList();
        Assert.Equal(["https://example.com/dir/paint.js", "https://example.com/dir/lib.js"], requests.Select(request => request.Url));
        Assert.All(requests, request =>
        {
            Assert.Equal(RequestDestination.PaintWorklet, request.Destination);
            Assert.Equal(RequestMode.Cors, request.Mode);
        });
        Assert.Equal("https://example.com/dir/page.html", requests[0].Referrer);
        Assert.Equal("https://example.com/dir/paint.js", requests[1].Referrer);
        Assert.Same(document.PaintWorklet, document.PaintWorklet);
    }

    [Fact]
    public void AddModule_RejectsWhenTheModuleCannotBeLoaded()
    {
        AddScript("https://example.com/dir/syntax.js", "this is not a script");
        AddScript("https://example.com/dir/throws.js", "console.log('before'); throw new Error('boom');");
        var document = Load("""
            <script>
              const results = [];
              const add = (url) => CSS.paintWorklet.addModule(url).then(() => 'resolved', error => error.name + ' ' + (error instanceof (error.name === 'SyntaxError' && url === 'syntax.js' ? SyntaxError : DOMException)));
              Promise.all([add('missing.js'), add('http://invalid:123$'), add('syntax.js'), add('throws.js'), add('about:blank')]).then(values => document.title = values.join('|'));
            </script>
            """);

        // A module that throws is still added; its error is reported to the host for each global scope
        Assert.Equal("AbortError true|SyntaxError true|SyntaxError true|resolved|AbortError true", document.Title);
        Assert.Equal(2, _errors.Count);
        Assert.All(_errors, error => Assert.Contains("boom", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void RegisterPaint_ValidatesTheClass()
    {
        AddScript("https://example.com/dir/paint.js", """
            const results = [];
            const register = (name, value) => {
              try { registerPaint(name, value); results.push('ok'); } catch (e) { results.push(e.name); }
            };
            register('', class { paint() {} });
            register('a', class { paint() {} });
            register('a', class { paint() {} });
            register('b', class { static get inputArguments() { return ['<length'] } paint() {} });
            register('c', class { static get inputProperties() { throw new RangeError('x'); } paint() {} });
            register('d', class { static get contextOptions() { return 5; } paint() {} });
            register('e', () => {});
            register('f', class { });
            register('g', class { static get inputProperties() { return ['--a', 'color', 'unknown-property']; } static get inputArguments() { return ['<length>', '*']; } static get contextOptions() { return { alpha: false }; } paint() {} });
            register('h', 5);
            console.log(results.join());
            """);
        var document = Load("<script>CSS.paintWorklet.addModule('paint.js').then(() => document.title = 'done');</script>");

        Assert.Equal("done", document.Title);
        Assert.Empty(_errors);
        Assert.All(WorkletMessages, message => Assert.Equal("TypeError,ok,InvalidModificationError,TypeError,RangeError,TypeError,TypeError,TypeError,ok,TypeError", message));
        Assert.Equal(2, WorkletMessages.Count());
    }

    [Fact]
    public void DynamicImport_IsNotAllowed()
    {
        AddScript("https://example.com/dir/paint.js", "import('./lib.js').then(() => console.log('imported'), error => console.log(error.name));");
        AddScript("https://example.com/dir/lib.js", "export const value = 42;");
        var document = Load("<script>CSS.paintWorklet.addModule('paint.js').then(() => document.title = 'done');</script>");

        Assert.Equal("done", document.Title);
        Assert.Equal(["TypeError", "TypeError"], WorkletMessages);
        Assert.DoesNotContain(_loader.Requests, request => request.Url.EndsWith("lib.js", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalScopes_AreTerminatedWithTheDocument()
    {
        AddScript("https://example.com/dir/paint.js", "console.log('run');");
        var document = Load("<iframe id=frame srcdoc=\"<script>CSS.paintWorklet.addModule('https://example.com/dir/paint.js').then(() => parent.document.title = 'added');</script>\"></iframe>");
        Assert.Equal("added", document.Title);

        var frame = (HtmlIFrameElement)document.GetElementById("frame")!;
        var worklet = frame.ContentDocument!.PaintWorklet;
        Assert.Equal(2, worklet.GlobalScopes.Count);
        var globalScope = worklet.GlobalScopes[0];
        Assert.True(globalScope.CanRunScript);

        frame.Remove();
        Assert.Empty(worklet.GlobalScopes);
        Assert.False(globalScope.CanRunScript);
        Assert.Equal("AbortError", Assert.IsType<DomException>(worklet.AddModule("https://example.com/dir/paint.js").Exception!.InnerException).Name);
    }

    [Fact]
    public void AddModule_WithoutScriptEngine_Fails()
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = _loader });
        var document = context.Navigate("https://example.com/", _ => "<p>");
        var task = document.PaintWorklet.AddModule("paint.js");
        Assert.Equal("AbortError", Assert.IsType<DomException>(task.Exception!.InnerException).Name);
        Assert.Equal("SyntaxError", Assert.IsType<DomException>(document.PaintWorklet.AddModule("http://invalid:123$").Exception!.InnerException).Name);
    }

    [Fact]
    public void PaintFunction_IsParsedAndSerialized()
    {
        var document = Load("""
            <div id=target style="background-image: paint( my-paint , 1px,  red ); list-style-image: paint(green); border-image-source: paint(a,); mask-image: paint(); cursor: paint(a), auto"></div>
            <script>
              const style = document.getElementById('target').style;
              const computed = getComputedStyle(document.getElementById('target'));
              document.title = [style.backgroundImage, style.listStyleImage, style.borderImageSource, style.maskImage, computed.backgroundImage, computed.listStyleImage, CSS.supports('background-image', 'paint(a, {})'), CSS.supports('background-image', 'paint(1)')].join('|');
            </script>
            """);

        Assert.Equal("paint(my-paint, 1px, red)|paint(green)|paint(a,)||paint(my-paint, 1px, red)|paint(green)|true|false", document.Title);
    }

    [Fact]
    public void ComputedStyleMap_ReifiesTheRegisteredCustomProperties()
    {
        var document = Load("""
            <div id=target style="--length: 2em; --list: 1px 2px; --ident: foo; --image: url(a.png); --unregistered: 1px"></div>
            <script>
              for (const [name, syntax, initialValue] of [['--length', '<length>', '0px'], ['--list', '<length>+', '0px'], ['--ident', 'foo | bar', 'bar'], ['--image', '<image> | none', 'none']])
                CSS.registerProperty({ name, syntax, initialValue, inherits: false });
              const map = document.getElementById('target').computedStyleMap();
              const serialize = name => map.getAll(name).map(value => value.constructor.name + ' ' + value).join(',');
              document.title = ['--length', '--list', '--ident', '--image', '--unregistered'].map(serialize).join('|');
            </script>
            """);

        Assert.Equal("CSSUnitValue 32px|CSSUnitValue 1px,CSSUnitValue 2px|CSSKeywordValue foo|CSSImageValue url(\"https://example.com/dir/a.png\")|CSSUnparsedValue 1px", document.Title);
    }

    [Fact]
    public void DisabledFeature_HidesThePaintWorklet()
    {
        var document = Load("<div style='background-image: paint(a)'></div><script>document.title = [typeof Worklet, typeof CSS.paintWorklet, 'paintWorklet' in CSS].join()</script>", new WebPlatformFeatures { PaintWorkletEnabled = false });
        Assert.Equal("function,undefined,false", document.Title);
    }

    [Fact]
    public void DisabledFeatures_HideTheWorkletInterface()
    {
        // The Worklet interface is exposed while a kind of worklet is enabled
        var document = Load("<script>document.title = [typeof Worklet, typeof CSS.paintWorklet, typeof CSS.layoutWorklet, typeof CSS.animationWorklet].join()</script>",
            new WebPlatformFeatures { PaintWorkletEnabled = false, LayoutWorkletEnabled = false, AnimationWorkletEnabled = false });
        Assert.Equal("undefined,undefined,undefined,undefined", document.Title);
    }

    [Fact]
    public void AudioWorklet_ProcessesTheAudioOfANode()
    {
        AddScript("https://example.com/dir/processor.js", """
            class Doubler extends AudioWorkletProcessor {
              static get parameterDescriptors() { return [{ name: 'gain', defaultValue: 2, minValue: 0, maxValue: 10 }]; }
              constructor(options) {
                super();
                this.offset = options.processorOptions.offset;
                this.port.postMessage([options.numberOfInputs, options.numberOfOutputs, sampleRate, currentFrame].join());
              }
              process(inputs, outputs, parameters) {
                const input = inputs[0], output = outputs[0], gain = parameters.gain;
                for (let c = 0; c < output.length; ++c) {
                  for (let i = 0; i < output[c].length; ++i) {
                    output[c][i] = (input.length ? input[c][i] : 0) * gain[gain.length > 1 ? i : 0] + this.offset;
                  }
                }
                return Object.isFrozen(inputs) && Object.isFrozen(inputs[0]) && Object.isFrozen(parameters);
              }
            }
            registerProcessor('doubler', Doubler);
            """);
        var document = Load("""
            <script>
              const context = new OfflineAudioContext(1, 256, 8000);
              const messages = [];
              context.audioWorklet.addModule('processor.js').then(() => {
                const node = new AudioWorkletNode(context, 'doubler', { parameterData: { gain: 3 }, processorOptions: { offset: 0.5 } });
                node.port.onmessage = e => messages.push(e.data);
                const source = new ConstantSourceNode(context, { offset: 0.25 });
                source.connect(node).connect(context.destination);
                source.start();
                return context.startRendering().then(buffer => {
                  const data = buffer.getChannelData(0);
                  document.title = [data[0], data[255], node.parameters.size, node.parameters.get('gain').defaultValue, ...messages].join(';');
                });
              }).catch(e => document.title = 'error ' + e);
            </script>
            """);

        Assert.Equal("1.25;1.25;1;2;1,1,8000,0", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void AudioWorklet_FiresProcessorErrorWhenTheProcessorThrows()
    {
        AddScript("https://example.com/dir/processor.js", """
            registerProcessor('throws', class extends AudioWorkletProcessor { process() { throw new Error('boom'); } });
            registerProcessor('fails', class extends AudioWorkletProcessor { constructor() { super(); throw new TypeError('ctor'); } });
            try { registerProcessor('throws', class extends AudioWorkletProcessor {}); } catch (e) { console.log(e.name); }
            try { registerProcessor('', class extends AudioWorkletProcessor {}); } catch (e) { console.log(e.name); }
            try { new AudioWorkletProcessor(); } catch (e) { console.log(e.name); }
            """);
        var document = Load("""
            <script>
              const context = new OfflineAudioContext(1, 256, 8000);
              const events = [];
              context.audioWorklet.addModule('processor.js').then(() => {
                const throws = new AudioWorkletNode(context, 'throws');
                throws.onprocessorerror = e => events.push('process:' + (e instanceof ErrorEvent) + ':' + e.message);
                const fails = new AudioWorkletNode(context, 'fails');
                fails.onprocessorerror = e => events.push('constructor:' + e.message);
                throws.connect(context.destination);
                try { new AudioWorkletNode(context, 'unknown'); } catch (e) { events.push(e.name); }
                return context.startRendering();
              }).then(buffer => setTimeout(() => document.title = events.join(';') + ';' + buffer.getChannelData(0)[0], 0));
            </script>
            """);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("InvalidStateError;constructor:ctor;process:true:boom;0", document.Title.Replace("TypeError: ", "", StringComparison.Ordinal).Replace("Error: ", "", StringComparison.Ordinal));
        Assert.Equal(["NotSupportedError", "NotSupportedError", "TypeError"], WorkletMessages);
    }
}
