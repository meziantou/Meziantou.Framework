using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The global object of a worklet (https://html.spec.whatwg.org/multipage/worklets.html#workletglobalscope), e.g. a
/// <see cref="PaintWorkletGlobalScope"/>: the module scripts of its <see cref="Worklet"/> run in its realm, which only
/// exposes the interfaces of its kind (no DOM, no timers, no network access).
/// </summary>
/// <remarks>
/// The realm of a worklet global scope belongs to the script engine of the document of the worklet: its scripts and its
/// callbacks run on the thread of the event loop of the document.
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/worklets.html#workletglobalscope"/>
[IdlInterface("WorkletGlobalScope", "https://html.spec.whatwg.org/multipage/worklets.html#workletglobalscope")]
public abstract class WorkletGlobalScope : GlobalScope
{
    // https://html.spec.whatwg.org/multipage/worklets.html#set-up-a-worklet-environment-settings-object: a unique opaque origin
    private readonly OriginRecord _origin = OriginRecord.CreateOpaque();

    private protected WorkletGlobalScope(Worklet worklet)
        : base(worklet.Document.EnvironmentOptions!, worklet.Document.EnvironmentEventLoop!)
    {
        Worklet = worklet;
    }

    /// <summary>The worklet of the global scope.</summary>
    internal Worklet Worklet { get; }

    /// <summary>Whether the global scope is terminated (https://html.spec.whatwg.org/multipage/worklets.html#terminate-a-worklet-global-scope): its scripts no longer run.</summary>
    internal bool IsTerminated { get; set; }

    internal override ScriptEngine? ScriptEngine => Options.ScriptEngine;

    // https://html.spec.whatwg.org/multipage/worklets.html#set-up-a-worklet-environment-settings-object: the API base URL of
    // the outside settings
    internal override UrlRecord ApiBaseUrl => Worklet.Document.DocumentBaseUrl;

    internal override string ScriptUrl => Worklet.Document.Url;

    internal override OriginRecord OriginRecord => _origin;

    internal override bool CanRunScript => !IsTerminated && Worklet.Document.IsFullyActive;

    internal override bool IsScriptingEnabled => true;

    /// <summary>
    /// The document of the worklet: the fetch client of the module scripts is the outside settings object
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-worklet-script-graph), and a worklet global scope makes
    /// no other request. Its policy container is the one of the document.
    /// </summary>
    internal override Document EnvironmentDocument => Worklet.Document;

    internal override void QueueGlobalTask(Action task)
    {
        if (IsTerminated)
            return;

        EventLoop.QueueTask(() =>
        {
            if (CanRunScript)
            {
                task();
            }
        });
    }

    /// <inheritdoc/>
    [NotInIdl("WorkletGlobalScope does not include WindowOrWorkerGlobalScope: the origin of a worklet is an opaque origin")]
    public override string Origin => "null";

    /// <inheritdoc/>
    [NotInIdl("WorkletGlobalScope does not include WindowOrWorkerGlobalScope: the worklets only exist in the secure contexts")]
    public override bool IsSecureContext => Worklet.Document.RelevantWindow?.IsSecureContext ?? false;

    /// <inheritdoc/>
    /// <remarks>
    /// https://html.spec.whatwg.org/multipage/worklets.html#set-up-a-worklet-environment-settings-object: the cross-origin
    /// isolated capability is the one of the document (e.g. a <c>SharedArrayBuffer</c> can be shared with the audio worklet
    /// of a cross-origin isolated document).
    /// </remarks>
    [NotInIdl("WorkletGlobalScope does not include WindowOrWorkerGlobalScope")]
    public override bool CrossOriginIsolated => Worklet.Document.RelevantWindow?.CrossOriginIsolated ?? false;

    // https://html.spec.whatwg.org/multipage/webappapis.html#obtain-a-worklet-agent: a worklet agent is in the agent cluster
    // of the document, so that the shared memory and the WebAssembly modules can be sent to it
    internal override object? AgentClusterKey => Worklet.Document.RelevantWindow?.AgentClusterKey ?? this;

    internal override bool IsAgentClusterCrossOriginIsolated => Worklet.Document.RelevantWindow?.IsAgentClusterCrossOriginIsolated ?? false;
}
