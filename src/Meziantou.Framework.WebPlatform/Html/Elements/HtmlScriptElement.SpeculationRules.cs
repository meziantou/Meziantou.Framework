using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/webappapis.html#speculation-rules-parse-result: the result of a
/// <c>&lt;script type=speculationrules&gt;</c> element.
/// </summary>
internal sealed class SpeculationRulesParseResult
{
    public SpeculationRuleSet? RuleSet { get; init; }

    /// <summary>The exception of the parsing (a <c>SyntaxError</c> or a <c>TypeError</c>), which prevents using the rules.</summary>
    public Exception? ErrorToRethrow { get; init; }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#create-a-speculation-rules-parse-result</summary>
    public static SpeculationRulesParseResult Create(string input, Document document)
    {
        try
        {
            return new SpeculationRulesParseResult { RuleSet = SpeculationRuleSet.Parse(input, document, document.DocumentBaseUrl) };
        }
        catch (System.Text.Json.JsonException ex)
        {
            return new SpeculationRulesParseResult { ErrorToRethrow = new JavaScriptSyntaxErrorException("The speculation rules are not valid JSON: " + ex.Message) };
        }
        catch (TypeErrorException ex)
        {
            return new SpeculationRulesParseResult { ErrorToRethrow = ex };
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#register-speculation-rules</summary>
    public void Register(Window window, bool queueErrors = false)
    {
        if (ErrorToRethrow is { } error)
        {
            if (queueErrors)
            {
                window.Document.QueueGlobalTask(() => Scripting.ScriptErrors.Report(error, window));
            }
            else
            {
                Scripting.ScriptErrors.Report(error, window);
            }

            return;
        }

        window.Document.AddSpeculationRuleSet(RuleSet!);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#unregister-speculation-rules</summary>
    public void Unregister(Window window)
    {
        if (ErrorToRethrow is null)
        {
            window.Document.RemoveSpeculationRuleSet(RuleSet!);
        }
    }
}

public partial class HtmlScriptElement
{
    /// <summary>Whether the speculation rules are supported in the current realm (see <see cref="WebPlatformFeatures.SpeculationRulesEnabled"/>).</summary>
    private static bool IsSpeculationRulesSupported => Document.CurrentGlobalDocument?.BrowsingContext?.Options.EffectiveFeatures.SpeculationRulesEnabled ?? true;

    /// <summary>The window in which the speculation rules of the element are registered.</summary>
    private Window? _speculationRulesWindow;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element, for an inline
    /// <c>speculationrules</c> script: its result is a speculation rules parse result, which is registered when the element
    /// is executed.
    /// </summary>
    private static SpeculationRulesParseResult CreateSpeculationRulesResult(string sourceText, Document document) => SpeculationRulesParseResult.Create(sourceText, document);

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#execute-the-script-element, for a speculation rules parse result.</summary>
    private void RegisterSpeculationRules(SpeculationRulesParseResult result, Window window)
    {
        _speculationRulesWindow = window;
        window.Document.AddSpeculationRulesScript(this);
        result.Register(window);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dynamic-changes-to-base-urls: when the document base URL
    /// changes, the speculation rules of the element are parsed again from its child text content, with the new base URL
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#update-speculation-rules). The errors of the new rules are
    /// reported by a task. The changes of the children of the element do not update its rules.
    /// </summary>
    internal void UpdateSpeculationRules()
    {
        if (Result is not SpeculationRulesParseResult oldResult || _speculationRulesWindow is not { } window)
            return;

        var newResult = SpeculationRulesParseResult.Create(GetChildTextContent(), NodeDocument);
        oldResult.Unregister(window);
        newResult.Register(window, queueErrors: true);
        Result = newResult;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model: the removing steps unregister the speculation rules.</summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);

        // https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism: a render-blocking element that becomes
        // browsing-context disconnected no longer blocks the rendering
        StopBlockingRendering();
        if (Result is SpeculationRulesParseResult result && _speculationRulesWindow is { } window)
        {
            _speculationRulesWindow = null;
            window.Document.RemoveSpeculationRulesScript(this);
            result.Unregister(window);
        }
    }
}
