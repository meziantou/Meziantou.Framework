using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// A list of container conditions evaluated for an element, whose state can be observed, created by
/// <see cref="Element.MatchContainer"/>. The API is proposed in https://github.com/w3c/csswg-drafts/pull/13551 and is
/// modeled on <see cref="MediaQueryList"/>.
/// </summary>
/// <remarks>
/// The conditions are evaluated like the conditions of a <c>@container</c> rule that applies to the element (see
/// <see cref="ContainerQueries"/>): the list matches when the element is connected and one of its conditions is true. The
/// <c>change</c> events are fired when the event loop updates the rendering, after the <c>change</c> events of the media
/// query lists, while the document of the element is fully active.
/// </remarks>
[IdlInterface("ContainerQueryList", "https://drafts.csswg.org/css-conditional-5/#containerquerylist")]
[Experimental(ExperimentalApis.MatchContainer, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class ContainerQueryList : EventTarget, IRealmBoundPlatformObject
{
    /// <summary>The container query lists with event listeners, kept alive as long as their element.</summary>
    private static readonly ConditionalWeakTable<Element, List<ContainerQueryList>> ListsWithListeners = [];

    private readonly List<ContainerCondition> _conditions;
    private readonly Window? _window;
    private bool _keptAlive;

    internal ContainerQueryList(Element element, string query)
    {
        Element = element;
        _window = element.NodeDocument.RelevantWindow;
        _conditions = Parse(query) ?? [];
        Query = string.Join(", ", _conditions.Select(c => c.NameText.Length == 0 ? c.QueryText : c.QueryText.Length == 0 ? c.NameText : c.NameText + " " + c.QueryText));
        LastMatches = Matches;
        element.NodeDocument.EventLoop?.AddContainerQueryList(this);
    }

    /// <summary>The element whose query containers are queried.</summary>
    internal Element Element { get; }

    /// <summary>The matches state when the changes were last reported.</summary>
    internal bool LastMatches { get; set; }

    internal override Window? RelevantGlobal => _window;

    Window? IRealmBoundPlatformObject.RealmWindow => _window;

    /// <summary>Gets the serialized container conditions, or the empty string when the query is invalid.</summary>
    [IdlMember("query", "https://drafts.csswg.org/css-conditional-5/#dom-containerquerylist-query")]
    public string Query { get; }

    /// <summary>
    /// Gets whether one of the container conditions is true for the element; false when the element is not connected. The size
    /// queries are evaluated with the sizes of the containers computed by the layout (a forced layout when the element is in a
    /// size container).
    /// </summary>
    [IdlMember("matches", "https://drafts.csswg.org/css-conditional-5/#dom-containerquerylist-matches")]
    public bool Matches
    {
        get
        {
            if (_conditions.Count == 0 || !Element.IsConnected)
                return false;

            ContainerQueries.GetStyleWithContainerLayout(Element, pseudoElement: null);
            var engine = Element.NodeDocument.GetStyleEngine();
            return _conditions.Exists(condition => ContainerQueries.Evaluate(condition, engine, Element, pseudoElement: null) == true);
        }
    }

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    [IdlMember("onchange", "https://drafts.csswg.org/css-conditional-5/#dom-containerquerylist-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>Parses a comma-separated list of &lt;container-condition&gt;s; returns null when one of them is invalid.</summary>
    private static List<ContainerCondition>? Parse(string query)
    {
        var values = CssParser.ParseListOfComponentValues(query, out var source);
        var result = new List<ContainerCondition>();
        foreach (var part in Values.ValueMatcher.SplitAtCommas(values))
        {
            if (ContainerQueryParser.ParseCondition(part, source) is not { } condition)
                return null;

            result.Add(condition);
        }

        return result;
    }

    /// <summary>
    /// Evaluates the container query lists and fires a <c>change</c> event at the lists whose matches state changed since it
    /// was last reported. The states are updated before the events are fired, so that the state of every list is up to date
    /// in the event listeners. The lists whose element is in a document that is not fully active are skipped.
    /// </summary>
    internal static void EvaluateAndReportChanges(List<WeakReference<ContainerQueryList>> lists)
    {
        lists.RemoveAll(reference => !reference.TryGetTarget(out _));
        var changed = new List<ContainerQueryList>();
        foreach (var reference in lists.ToArray())
        {
            if (!reference.TryGetTarget(out var list) || !list.Element.NodeDocument.IsFullyActive)
                continue;

            var matches = list.Matches;
            if (matches != list.LastMatches)
            {
                list.LastMatches = matches;
                changed.Add(list);
            }
        }

        foreach (var list in changed)
        {
            list.FireEvent(new Event("change"));
        }
    }

    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        // A container query list with event listeners must not be garbage collected while its element is alive
        if (!_keptAlive)
        {
            _keptAlive = true;
            ListsWithListeners.GetOrCreateValue(Element).Add(this);
        }
    }
}
