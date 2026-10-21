using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>The navigation API (https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api).</summary>
public sealed class NavigationApiTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        for (var i = 0; i < 5; i++)
        {
            _contexts.EventLoop.RunUntilIdle();
            Thread.Sleep(10);
        }

        Assert.Empty(_errors);
        return context.Document;
    }

    [Fact]
    public void Navigate_FragmentNavigation_FiresNavigateAndCurrentEntryChange()
    {
        var document = Load("""
            <script>
              const log = [];
              navigation.onnavigate = e => log.push('navigate ' + e.navigationType + ' ' + e.hashChange + ' ' + new URL(e.destination.url).hash);
              navigation.oncurrententrychange = e => log.push('currententrychange ' + e.navigationType + ' ' + e.from.index);
              navigation.onnavigatesuccess = () => log.push('navigatesuccess');
              window.onload = () => setTimeout(async () => {
                const result = navigation.navigate('#a', { state: { value: 1 } });
                const entry = await result.finished;
                log.push(entry === navigation.currentEntry, navigation.entries().length, navigation.currentEntry.getState().value);
                document.title = log.join();
              }, 0);
            </script>
            """);

        Assert.Equal("navigate push true #a,currententrychange push 0,navigatesuccess,true,2,1", document.Title);
    }

    [Fact]
    public void Intercept_ConvertsCrossDocumentNavigationIntoSameDocumentNavigation()
    {
        var document = Load("""
            <script>
              const log = [];
              navigation.onnavigate = e => e.intercept({ handler: async () => { log.push('handler ' + location.pathname); } });
              window.onload = () => setTimeout(async () => {
                await navigation.navigate('/other').finished;
                log.push(location.pathname, navigation.entries().length, navigation.transition);
                document.title = log.join();
              }, 0);
            </script>
            """);

        Assert.Equal("handler /other,/other,2,", document.Title);
        Assert.Empty(_loader.Requests);
    }

    [Fact]
    public void PreventDefault_CancelsTheNavigation()
    {
        var document = Load("""
            <script>
              navigation.onnavigate = e => e.preventDefault();
              navigation.onnavigateerror = e => document.title = e.error.name + '|' + location.hash;
              window.onload = () => setTimeout(() => location.hash = 'b', 0);
            </script>
            """);

        Assert.Equal("AbortError|", document.Title);
    }

    [Fact]
    public void Back_TraversesToThePreviousEntry()
    {
        var document = Load("""
            <script>
              window.onload = () => setTimeout(async () => {
                await navigation.navigate('#1').finished;
                await navigation.navigate('#2').finished;
                const key = navigation.entries()[1].key;
                await navigation.back().finished;
                document.title = [location.hash, navigation.currentEntry.index, navigation.currentEntry.key === key, navigation.canGoForward].join();
              }, 0);
            </script>
            """);

        Assert.Equal("#1,1,true,true", document.Title);
    }

    [Fact]
    public void PushState_FiresASameDocumentNavigateEvent()
    {
        var document = Load("""
            <script>
              navigation.onnavigate = e => document.title = [e.navigationType, e.destination.sameDocument, e.canIntercept, e.hashChange].join();
              history.pushState(null, '', '/page');
            </script>
            """);

        Assert.Equal("push,true,true,false", document.Title);
    }

    [Fact]
    public void NavigateError_OfARejectedHandler_HasTheErrorStringAndTheLocationOfTheError()
    {
        var document = Load("""
            <script>
              const error = new TypeError('a message');
              navigation.onnavigate = e => e.intercept({ handler: () => Promise.reject(error) });
              navigation.onnavigateerror = e => document.title = [e.message, e.filename, e.lineno > 0, e.colno > 0, e.error === error].join('|');
              window.onload = () => setTimeout(() => navigation.navigate('#a').finished.catch(() => { }), 0);
            </script>
            """);

        Assert.Equal("TypeError: a message|https://example.com/|true|true|true", document.Title);
    }

    [Fact]
    public void NavigateError_OfAnAbortedNavigation_IsLocatedAtTheScriptThatAbortsIt()
    {
        var document = Load("""
            <script>
              navigation.onnavigate = e => e.intercept({ handler: () => new Promise(() => { }) });
              navigation.onnavigateerror = e => document.title = [e.error.name, e.filename, e.lineno > 0, e.colno > 0].join('|');
              window.onload = () => setTimeout(() => {
                navigation.navigate('?1');
                window.stop();
              }, 0);
            </script>
            """);

        Assert.Equal("AbortError|https://example.com/|true|true", document.Title);
    }

    [Fact]
    public void RemovedIframe_RejectsThePromisesWithAnAbortErrorOfTheRealmOfTheIframe()
    {
        _loader.Resources["https://example.com/frame"] = new DocumentResource("<p>frame</p>");
        var document = Load("""
            <iframe id="i" src="/frame"></iframe>
            <script>
              window.onload = async () => {
                const frameWindow = i.contentWindow;
                const frameDOMException = frameWindow.DOMException;
                frameWindow.navigation.onnavigate = () => i.remove();
                try {
                  await frameWindow.navigation.navigate('#1').committed;
                } catch (e) {
                  document.title = [e.name, e.constructor === frameDOMException, e instanceof DOMException].join();
                }
              };
            </script>
            """);

        Assert.Equal("AbortError,true,false", document.Title);
    }

    [Fact]
    public void PushInAChildNavigable_DisposesTheForwardEntriesOfItsParent()
    {
        _loader.Resources["https://example.com/frame"] = new DocumentResource("<p>frame</p>");
        var document = Load("""
            <iframe id="i" src="/frame"></iframe>
            <script>
              window.onload = () => setTimeout(async () => {
                const child = i.contentWindow.navigation;
                await child.navigate('#a').finished;
                await navigation.navigate('#1').finished;
                await child.back().finished;
                const entry = navigation.entries()[navigation.currentEntry.index + 1];
                const log = [location.hash, navigation.entries().length];
                entry.ondispose = () => {
                  log.push('dispose', navigation.entries().length, entry.index);
                  document.title = log.join();
                };
                await child.navigate('#b').finished;
              }, 0);
            </script>
            """);

        Assert.Equal(",2,dispose,1,-1", document.Title);
    }

    [Fact]
    public void ManualScroll_KeepsTheTargetElementUntilScroll()
    {
        var document = Load("""
            <div id="frag"></div>
            <script>
              window.onload = () => setTimeout(async () => {
                await navigation.navigate('#frag').finished;
                let event;
                navigation.onnavigate = e => {
                  event = e;
                  e.intercept({ scroll: 'manual', handler: () => new Promise(() => { }) });
                };
                await navigation.navigate('/').committed;
                const log = [location.hash, document.querySelector(':target')?.id];
                event.scroll();
                log.push(document.querySelector(':target'));
                document.title = log.join();
              }, 0);
            </script>
            """);

        Assert.Equal(",frag,", document.Title);
    }

    [Fact]
    public void PushState_DoesNotChangeTheTargetElement()
    {
        var document = Load("""
            <div id="frag"></div>
            <script>
              history.pushState(null, '', '#frag');
              const log = [location.hash, document.querySelector(':target')];
              location.hash = '#frag';
              location.hash = '#other';
              location.hash = '#frag';
              log.push(document.querySelector(':target').id);
              document.title = log.join();
            </script>
            """);

        Assert.Equal("#frag,,frag", document.Title);
    }

    [Fact]
    public void Intercept_ResetsTheClassicHistoryState()
    {
        var document = Load("""
            <script>
              history.replaceState('state', '', '#1');
              navigation.onnavigate = e => e.intercept();
              window.onload = () => setTimeout(async () => {
                await navigation.navigate('#2').committed;
                document.title = [location.hash, history.state].join();
              }, 0);
            </script>
            """);

        Assert.Equal("#2,", document.Title);
    }

    [Fact]
    public void Traversal_StartedByTheNavigateEventOfATraversal_IsAppliedAfterIt()
    {
        var document = Load("""
            <script>
              window.onload = () => setTimeout(async () => {
                await navigation.navigate('#1').finished;
                await navigation.navigate('#2').finished;
                await navigation.navigate('#3').finished;

                // The hashchange events of the navigations are fired by tasks
                await new Promise(resolve => setTimeout(resolve, 0));
                const key = navigation.entries()[0].key;
                const log = [];
                navigation.onnavigate = () => {
                  navigation.onnavigate = null;
                  navigation.traverseTo(key);
                };
                window.onhashchange = () => {
                  log.push(navigation.currentEntry.index);
                  document.title = log.join();
                };
                history.back();
              }, 0);
            </script>
            """);

        Assert.Equal("2,0", document.Title);
    }

    [Fact]
    public void Back_InTheNavigateEventOfATraversalToTheSameEntry_IsRejected()
    {
        var document = Load("""
            <script>
              window.onload = () => setTimeout(async () => {
                await navigation.navigate('#1').finished;
                navigation.onnavigate = () => {
                  navigation.onnavigate = null;
                  const result = navigation.back();
                  result.committed.catch(() => { });
                  result.finished.catch(e => document.title = [e.name, location.hash, navigation.currentEntry.index].join());
                };
                history.back();
              }, 0);
            </script>
            """);

        Assert.Equal("InvalidStateError,,0", document.Title);
    }

    [Fact]
    public void Navigate_ToAFileUrlFromTheWeb_IsAborted()
    {
        var document = Load("""
            <script>
              let fired = false;
              navigation.onnavigate = () => fired = true;
              const result = navigation.navigate('file:///file.html');
              result.finished.catch(() => { });
              result.committed.catch(e => document.title = [e.name, fired].join());
            </script>
            """);

        Assert.Equal("AbortError,false", document.Title);
        Assert.Empty(_loader.Requests);
    }

    [Fact]
    public void PostFormSubmission_ToTheUrlOfTheDocument_IsAPush()
    {
        var document = Load("""
            <form method="post" action=""><input name="a" value="b"></form>
            <script>
              navigation.onnavigate = e => {
                e.preventDefault();
                document.title = [e.navigationType, e.formData.get('a')].join();
              };
              window.onload = () => setTimeout(() => document.forms[0].submit(), 0);
            </script>
            """);

        Assert.Equal("push,b", document.Title);
    }

    [Fact]
    public void MetaRefresh_ToTheUrlOfTheDocument_IsAReload()
    {
        var document = Load("""
            <meta http-equiv="refresh" content="0">
            <script>
              navigation.onnavigate = e => {
                e.preventDefault();
                document.title = [e.navigationType, e.destination.url].join();
              };
            </script>
            """);

        Assert.Equal("reload,https://example.com/", document.Title);
    }

    [Fact]
    public void DownloadRequest_AbortsTheOngoingNavigation()
    {
        var document = Load("""
            <script>
              const log = [];
              navigation.onnavigate = e => {
                log.push('navigate ' + e.downloadRequest);
                if (e.downloadRequest === null)
                  e.intercept({ handler: () => new Promise(() => { }) });
              };
              navigation.onnavigateerror = () => log.push('navigateerror');
              window.onload = () => setTimeout(() => {
                navigation.navigate('?1');
                navigation.transition.finished.catch(e => {
                  log.push(e.name);
                  document.title = log.slice(0, 3).join();
                });
                const a = document.createElement('a');
                a.href = '?download';
                a.download = 'file';
                document.body.append(a);
                a.click();
              }, 0);
            </script>
            """);

        Assert.Equal("navigate null,navigateerror,navigate file", document.Title);
    }

    [Fact]
    public void IframeLoadEvent_IsFiredBetweenTheLoadAndThePageShowEventsOfItsWindow()
    {
        _loader.Resources["https://example.com/frame"] = new DocumentResource("""
            <script>
              window.onload = () => parent.log.push('window load');
              window.onpageshow = () => parent.log.push('pageshow');
            </script>
            """);
        var document = Load("""
            <script>var log = [];</script>
            <iframe src="/frame" onload="log.push('iframe load'); setTimeout(() => document.title = log.join(), 0)"></iframe>
            """);

        Assert.Equal("window load,iframe load,pageshow", document.Title);
    }
}
