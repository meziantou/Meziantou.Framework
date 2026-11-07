using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Observable API (https://wicg.github.io/observable/), implemented in JavaScript (Observable.js): its algorithms are
// made of callbacks, promises, iterators and abort signals. The script is evaluated in a realm when Observable, Subscriber
// or EventTarget.prototype.when() is first used.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The interfaces defined by Observable.js, exposed on the global object ([Exposed=*]).</summary>
    internal static readonly string[] ObservableInterfaceNames = ["Observable", "Subscriber"];

    private static string? s_observableScript;
    private TValue? _observables;

    private void DefineObservableGlobals()
    {
        foreach (var name in ObservableInterfaceNames)
        {
            if (!IsInterfaceEnabled(name))
                continue;

            Adapter.DefineLazyDataProperty(Global, Key(name), () => Adapter.Get(GetObservables(), Key(name)), writable: true, enumerable: false, configurable: true);
        }
    }

    /// <summary>Defines EventTarget.prototype.when() (https://wicg.github.io/observable/#dom-eventtarget-when).</summary>
    private void DefineEventTargetObservableMembers(TValue prototype)
    {
        // The operation is defined after the members of EventTarget, where DefineOperation does not check the features
        if (!IsMemberEnabled("EventTarget", "when"))
            return;

        DefineOperation(prototype, "when", 1, static (realm, thisValue, arguments) =>
        {
            var target = realm.This<EventTarget>(thisValue, Generated.Binding_EventTarget<TAdapter, TValue>.Binding);
            var adapter = realm.Adapter;
            var when = adapter.Get(realm.GetObservables(), realm.Key("when"));
            return adapter.Call(when, adapter.Undefined, [realm.Wrap(target), realm.CreateArray(arguments.ToArray())]);
        });
    }

    /// <summary>Returns the object returned by Observable.js in this realm: the interface objects and the steps of when().</summary>
    private TValue GetObservables()
    {
        if (_observables is { } observables)
            return observables;

        return Adapter.RunInRealm(EngineRealm, () =>
        {
            if (_observables is { } existing)
                return existing;

            var script = s_observableScript ??= LoadEmbeddedScript("Meziantou.Framework.WebPlatform.Scripting.WebIdl.Observable.js");
            var adapter = Adapter;
            var interfaces = CreateObject();
            foreach (var name in (ReadOnlySpan<string>)["AbortController", "AbortSignal", "EventTarget"])
            {
                Adapter.Set(interfaces, Key(name), GetInterfaceObject(name));
            }

            var hooks = CreateObject();
            Adapter.Set(hooks, Key("interfaces"), interfaces);
            Adapter.Set(hooks, Key("internals"), Agent.ObservableInternals ??= EvaluateBuiltIn("new WeakMap()"));

            // https://dom.spec.whatwg.org/#abortsignal-add: returns a function that removes the algorithm
            // (https://dom.spec.whatwg.org/#abortsignal-remove). The exceptions of the algorithm are thrown by abort().
            Adapter.Set(hooks, Key("addAbortAlgorithm"), adapter.CreateFunction("addAbortAlgorithm", 2, (_, args) =>
            {
                var signal = (AbortSignal)Unwrap(args[0])!;
                var callback = args[1];
                void Algorithm() => adapter.Call(callback, adapter.Undefined, []);
                signal.AddAbortAlgorithm(Algorithm);
                return adapter.CreateFunction("removeAbortAlgorithm", 0, (_, _) =>
                {
                    signal.RemoveAbortAlgorithm(Algorithm);
                    return adapter.Undefined;
                });
            }));

            // https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception, with the global object of this realm:
            // an error object that was not thrown is located where it was created (as reportError() does), and another value
            // at the location given by Observable.js (the innermost frame of a script, e.g. the call of subscriber.error())
            Adapter.Set(hooks, Key("reportException"), adapter.CreateFunction("reportException", 2, (_, args) =>
            {
                var value = args.Length > 0 ? args[0] : adapter.Undefined;
                EventDispatcher.ReportedExceptionCount++;
                if (GlobalScope is WorkerGlobalScope { Agent.IsTerminated: true })
                    return adapter.Undefined;

                var reportedValue = adapter.IsUndefined(value) ? value : ToAny(value);
                var engine = GlobalScope.ScriptEngine;
                var error = engine?.ExtractErrorInformation(reportedValue) ?? new ScriptError(reportedValue?.ToString() ?? "undefined", reportedValue);
                if (error is { Filename.Length: 0, Error: not null } && args.Length > 1 && Adapter.IsObject(args[1]))
                {
                    var location = args[1];
                    error = new ScriptError(error.Message, error.Error)
                    {
                        Filename = adapter.ToString(adapter.Get(location, Key("0"))),
                        LineNumber = (int)adapter.ToNumber(adapter.Get(location, Key("1"))),
                        ColumnNumber = (int)adapter.ToNumber(adapter.Get(location, Key("2"))),
                    };
                }

                GlobalScope.ReportException(error);
                return adapter.Undefined;
            }));

            // The steps of the specification do nothing when the global object of the realm is a window whose document is
            // not fully active
            Adapter.Set(hooks, Key("isFullyActive"), adapter.CreateFunction("isFullyActive", 0, (_, _) => adapter.FromBoolean(Window is not { } window || window.Document.IsFullyActive)));

            var result = Adapter.Call(EvaluateBuiltIn(script), Adapter.Undefined, [hooks]);
            _observables = result;
            return result;
        });
    }
}
